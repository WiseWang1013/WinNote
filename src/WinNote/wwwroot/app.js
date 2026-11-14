'use strict';

/* ================= 数据与常量 ================= */

const COLORS = [
  { id: 'default', name: '默认', light: '#FFFFFF', dark: '#1B1C21' },
  { id: 'yellow', name: '米黄', light: '#FFF5D1', dark: '#3B331C' },
  { id: 'orange', name: '杏橙', light: '#FFE7D2', dark: '#3D2A1B' },
  { id: 'pink', name: '樱粉', light: '#FFE2EB', dark: '#3C2029' },
  { id: 'purple', name: '薰衣草', light: '#EEE6FF', dark: '#2C2442' },
  { id: 'blue', name: '天青', light: '#DFEEFF', dark: '#1A2C42' },
  { id: 'teal', name: '薄荷', light: '#DDF4EE', dark: '#16332D' },
  { id: 'green', name: '浅草', light: '#E4F6E2', dark: '#1E3420' },
  { id: 'gray', name: '雅灰', light: '#EFF0F3', dark: '#2A2B31' },
  { id: 'coral', name: '珊瑚', light: '#FFE2DC', dark: '#3D211C' },
];

const CATEGORIES = [
  { id: 'work', name: '工作', color: '#4C8DFF' },
  { id: 'life', name: '生活', color: '#34C759' },
  { id: 'study', name: '学习', color: '#FF9F0A' },
  { id: 'idea', name: '灵感', color: '#BF5AF2' },
];

const SMART_VIEWS = [
  { id: 'today', name: '今天', icon: 'i-note' },
  { id: 'week', name: '最近 7 天', icon: 'i-note' },
  { id: 'images', name: '带图片', icon: 'i-image' },
  { id: 'todo', name: '待办清单', icon: 'i-todo' },
  { id: 'locked', name: '已锁定', icon: 'i-lock' },
  { id: 'unfiled', name: '未分类', icon: 'i-folder' },
];

const state = {
  notes: [],
  settings: { theme: 'light', hasPasscode: false, ocrLanguage: 'zh-Hans', ocrLanguages: [], appearance: {} },
  appearance: {
    themeId: 'flat',
    accent: 'blue',
    radiusStyle: 'squircle',
    animations: true,
    topSafeInset: 0,
  },
  customThemes: [],
  customThemeFiles: [],
  settingsOpen: false,
  profile: { id: '', name: '本机', path: '', profiles: [] },
  folders: CATEGORIES.map((c) => ({ ...c })),
  tags: [],
  view: { type: 'all', category: null, tag: null, smart: null },  sort: 'updated',
  query: '',
  currentId: null,
  currentNote: null,
  dirty: false,
  saveTimer: null,
  searchTimer: null,
  selectedImage: null,
  lastRange: null,
  voiceOn: false,
  voiceRange: null,
  voiceNoteId: null,
  recording: false,
  recordTimer: null,
  recordStartedAt: 0,
  modalResolve: null,
  draw: { color: '#1A1A1C', width: 3, tool: 'pen', drawing: false, last: null, dpr: 1 },
};

const dom = {};
const pendingCalls = new Map();
let callSeq = 0;
let appearanceSaveTimer = null;

/* ================= 与 C# 通信 ================= */

function call(cmd, payload, timeout = 120000) {
  return new Promise((resolve, reject) => {
    const id = ++callSeq;
    const timer = setTimeout(() => {
      pendingCalls.delete(id);
      reject(new Error('操作超时'));
    }, timeout);
    pendingCalls.set(id, { resolve, reject, timer });
    window.chrome.webview.postMessage({ id, cmd, payload });
  });
}

function onBridgeMessage(event) {
  const message = event.data;
  if (!message) return;

  if (message.id && pendingCalls.has(message.id)) {
    const pending = pendingCalls.get(message.id);
    pendingCalls.delete(message.id);
    clearTimeout(pending.timer);
    if (message.ok) pending.resolve(message.data);
    else pending.reject(new Error(message.error || '操作失败'));
    return;
  }

  if (message.event) handleNativeEvent(message.event, message.data);
}

/* ================= 初始化 ================= */

window.addEventListener('DOMContentLoaded', init);

async function init() {
  cacheDom();
  bindEvents();
  updateThemeButton();

  if (!window.chrome || !window.chrome.webview) {
    document.body.innerHTML =
      '<div style="padding:48px;font-size:14px;line-height:2">请通过 WinNote.exe 打开备忘录。</div>';
    return;
  }

  window.chrome.webview.addEventListener('message', onBridgeMessage);
  window.__flushSave = flushSave;

  try {
    const data = await call('load');
    state.notes = (data.notes || []).map(normalizeNote);
    state.settings = Object.assign(
      {
        theme: 'light', hasPasscode: false, folders: [], ocrLanguage: 'zh-Hans', ocrLanguages: [],
        appearance: {}, appearanceDir: '', customThemes: [],
      },
      data.settings || {}
    );
    state.folders = normalizeFolders(state.settings.folders);
    state.profile = data.profile || { id: '', name: '本机', path: '', profiles: [] };
    state.appearance = normalizeAppearance(state.settings.appearance);
    state.settings.theme = state.appearance.mode;
    state.customThemeFiles = state.settings.customThemes || [];
    await loadCustomThemes(state.customThemeFiles);
    state.tags = collectTags();
    applyAppearance({ persist: false, syncChrome: true });
    renderSidebar();
    renderList();

    let startupFile = null;
    try {
      startupFile = await call('startupFile');
    } catch {
      startupFile = null;
    }

    if (startupFile) {
      try {
        await importFromPath(startupFile);
      } catch (error) {
        toast('打开文件失败：' + error.message, 'error');
        startupFile = null;
      }
    }

    if (!startupFile) {
      const first = visibleNotes()[0];
      if (first) {
        await openNote(first.id);
      } else {
        showEditorEmpty();
      }
    }
  } catch (error) {
    toast('加载失败：' + error.message, 'error');
  }

  document.body.classList.add('ready');
}

function cacheDom() {
  const ids = [
    'app', 'btnNew', 'btnImport', 'nav', 'countAll', 'countPinned', 'countTrash',
    'btnManageFolders', 'folders', 'tagList', 'smartList',
    'btnTheme', 'themeIcon', 'themeLabel', 'btnSettings', 'btnData', 'profileLabel', 'viewTitle', 'viewCount',
    'settingsPage', 'settingsBack', 'settingsDone', 'settingsNav', 'settingsSearch', 'settingsScroll',
    'themeGrid', 'accentGrid', 'radiusGrid', 'modeOptions', 'animToggle', 'activeThemeName',
    'customThemeList', 'btnImportTheme', 'btnImportThemeLibrary', 'btnOpenAppearance', 'appearancePath',
    'settingsProfileList', 'settingsDataPath', 'btnNewProfile', 'btnChooseProfile', 'btnRenameProfile',
    'btnOpenData', 'privacyPasscodeText', 'btnPrivacyPasscode', 'topInset', 'insetValue', 'btnMoveWindow',
    'btnEmptyTrash', 'btnSort', 'searchInput', 'btnClearSearch', 'noteList', 'listEmpty',
    'listEmptyText', 'btnNew2', 'editorPane', 'editorEmpty', 'btnNew3', 'editor',
    'noteTime', 'noteWords', 'saveState', 'btnCategoryChip', 'catChipDot',
    'catChipText', 'btnPin', 'btnColor', 'btnAi', 'btnExport', 'btnMore', 'editorScroll',
    'titleInput', 'tagRow', 'body', 'toolbar', 'btnVoice', 'btnInsert', 'voiceBar',
    'voiceTitle', 'voiceHypo', 'voiceStop', 'recordBar', 'recordTime', 'recordStop',
    'popoverLayer', 'toasts', 'modalLayer', 'modalCard', 'drawLayer',
    'drawClose', 'drawCanvas', 'penColors', 'drawModes', 'drawWidth', 'drawWidthLabel', 'drawClear',
    'drawCancel', 'drawInsert', 'printRoot',
  ];
  for (const id of ids) dom[id] = document.getElementById(id);
}

function normalizeFolders(raw) {
  const list = Array.isArray(raw) ? raw : [];
  const folders = list
    .filter((folder) => folder && folder.id)
    .map((folder) => ({
      id: String(folder.id),
      name: String(folder.name || '未命名文件夹').trim() || '未命名文件夹',
      color: /^#[0-9a-f]{6}$/i.test(folder.color || '') ? folder.color : '#4C8DFF',
    }));

  if (!folders.length) {
    return CATEGORIES.map((folder) => ({ ...folder }));
  }

  return folders;
}

function collectTags() {
  const counts = new Map();
  for (const note of state.notes) {
    if (note.deleted || note.locked) continue;
    for (const tag of note.tags || []) {
      const name = String(tag || '').trim();
      if (!name) continue;
      counts.set(name, (counts.get(name) || 0) + 1);
    }
  }

  return Array.from(counts.entries())
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name, 'zh-CN'));
}

/* ================= 事件绑定 ================= */

function bindEvents() {
  dom.btnNew.addEventListener('click', () => newNote());
  dom.btnNew2.addEventListener('click', () => newNote());
  dom.btnNew3.addEventListener('click', () => newNote());
  dom.btnImport.addEventListener('click', importFromDialog);
  dom.btnTheme.addEventListener('click', () => {
    applyTheme(state.settings.theme === 'dark' ? 'light' : 'dark', true);
  });
  dom.btnData.addEventListener('click', async () => {
    try {
      await call('openDataFolder');
    } catch (error) {
      toast(error.message, 'error');
    }
  });

  dom.nav.addEventListener('click', async (event) => {
    const button = event.target.closest('.nav-item');
    if (!button) return;
    await setView(button.dataset.view);
  });

  dom.folders.addEventListener('click', async (event) => {
    const button = event.target.closest('.cat-item');
    if (!button) return;
    await setView('category', button.dataset.folder);
  });

  dom.tagList.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-tag]');
    if (!button) return;
    await setView('tag', null, button.dataset.tag);
  });

  dom.smartList.addEventListener('click', async (event) => {
    const button = event.target.closest('.cat-item');
    if (!button) return;
    await setView('smart', null, null, button.dataset.smart);
  });

  dom.btnManageFolders.addEventListener('click', openFolderManager);
  dom.btnSettings.addEventListener('click', () => openSettingsPage());
  dom.btnSort.addEventListener('click', () => openSortPopover(dom.btnSort));

  dom.settingsBack.addEventListener('click', closeSettingsPage);
  dom.settingsDone.addEventListener('click', closeSettingsPage);
  dom.settingsNav.addEventListener('click', (event) => {
    const button = event.target.closest('[data-settings-section]');
    if (!button) return;
    scrollToSettingsSection(button.dataset.settingsSection);
  });
  dom.settingsSearch.addEventListener('input', filterSettingsSections);
  dom.themeGrid.addEventListener('click', (event) => {
    const card = event.target.closest('[data-theme-id]');
    if (!card) return;
    state.appearance.themeId = card.dataset.themeId;
    applyAppearance();
    renderAppearanceControls();
    renderSidebar();
  });
  dom.accentGrid.addEventListener('click', (event) => {
    const button = event.target.closest('[data-accent-id]');
    if (!button) return;
    state.appearance.accent = button.dataset.accentId;
    applyAppearance();
    renderAppearanceControls();
  });
  dom.radiusGrid.addEventListener('click', (event) => {
    const card = event.target.closest('[data-radius-id]');
    if (!card) return;
    state.appearance.radiusStyle = card.dataset.radiusId;
    applyAppearance();
    renderAppearanceControls();
  });
  dom.modeOptions.addEventListener('click', (event) => {
    const button = event.target.closest('[data-mode]');
    if (!button) return;
    applyTheme(button.dataset.mode, true);
    renderAppearanceControls();
  });
  dom.animToggle.addEventListener('click', () => {
    state.appearance.animations = !state.appearance.animations;
    applyAppearance();
    renderAppearanceControls();
  });
  dom.btnImportTheme.addEventListener('click', importCustomTheme);
  dom.btnImportThemeLibrary.addEventListener('click', importThemeLibrary);
  dom.btnOpenAppearance.addEventListener('click', () => {
    call('openAppearanceFolder').catch((error) => toast(error.message, 'error'));
  });
  dom.customThemeList.addEventListener('click', async (event) => {
    const use = event.target.closest('[data-use-theme]');
    if (use) {
      state.appearance.themeId = use.dataset.useTheme;
      applyAppearance();
      renderAppearanceControls();
      renderSidebar();
      return;
    }
    const remove = event.target.closest('[data-delete-theme]');
    if (remove) {
      await deleteCustomTheme(remove.dataset.deleteTheme);
    }
  });
  dom.btnNewProfile.addEventListener('click', createDataProfile);
  dom.btnChooseProfile.addEventListener('click', chooseDataProfile);
  dom.btnRenameProfile.addEventListener('click', renameCurrentProfile);
  dom.btnOpenData.addEventListener('click', () => {
    call('openDataFolder').catch((error) => toast(error.message, 'error'));
  });
  dom.settingsProfileList.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-switch-profile]');
    if (!button) return;
    await switchProfile(button.dataset.switchProfile);
    renderSettingsPage();
  });
  dom.btnPrivacyPasscode.addEventListener('click', async () => {
    await changePasscodeFlow();
    renderSettingsPage();
  });
  dom.btnMoveWindow.addEventListener('click', () => {
    call('moveWindow').catch((error) => toast(error.message, 'error'));
  });
  let insetTimer = null;
  dom.topInset.addEventListener('input', () => {
    const value = Number(dom.topInset.value) || 0;
    dom.insetValue.textContent = value + ' px';
    state.appearance.topSafeInset = value;
    clearTimeout(insetTimer);
    insetTimer = setTimeout(() => {
      call('setWindowInset', { topSafeInset: value }).catch((error) => toast(error.message, 'error'));
    }, 220);
  });

  dom.tagRow.addEventListener('click', async (event) => {
    const remove = event.target.closest('[data-remove-tag]');
    if (remove) {
      await removeTag(remove.dataset.removeTag);
      return;
    }

    if (event.target.closest('#btnAddTag')) {
      openTagPopover(dom.tagRow.querySelector('#btnAddTag') || dom.tagRow);
    }
  });

  dom.searchInput.addEventListener('input', () => {
    state.query = dom.searchInput.value;
    dom.btnClearSearch.classList.toggle('hidden', state.query.length === 0);
    clearTimeout(state.searchTimer);
    state.searchTimer = setTimeout(renderList, 120);
  });

  dom.btnClearSearch.addEventListener('click', () => {
    dom.searchInput.value = '';
    state.query = '';
    dom.btnClearSearch.classList.add('hidden');
    dom.searchInput.focus();
    renderList();
  });

  dom.btnEmptyTrash.addEventListener('click', emptyTrash);
  dom.noteList.addEventListener('click', onNoteListClick);

  dom.titleInput.addEventListener('input', () => {
    if (state.currentNote) {
      state.currentNote.title = dom.titleInput.value;
      updateCardInList(state.currentNote);
    }
    scheduleSave();
  });
  dom.titleInput.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      dom.body.focus();
      placeCaretAtEnd(dom.body);
    }
  });

  dom.body.addEventListener('input', () => {
    updateEmptyBody();
    scheduleSave();
  });
  dom.body.addEventListener('keydown', onBodyKeyDown);
  dom.body.addEventListener('click', onBodyClick);
  dom.body.addEventListener('paste', onBodyPaste);
  dom.body.addEventListener('dragover', onBodyDragOver);
  dom.body.addEventListener('drop', onBodyDrop);

  dom.toolbar.addEventListener('click', async (event) => {
    const button = event.target.closest('button[data-act]');
    if (!button) return;
    const act = button.dataset.act;
    if (act === 'todo') insertTodo();
    else if (act === 'image') await insertImageFromDisk();
    else if (act === 'voice') await toggleVoice();
    else if (act === 'ocr') await runOcr();
    else if (act === 'draw') openDraw();
    else if (act === 'insert') openInsertPopover(button);
  });

  dom.btnPin.addEventListener('click', togglePin);
  dom.btnColor.addEventListener('click', () => openColorPopover(dom.btnColor));
  dom.btnAi.addEventListener('click', () => openAiPopover(dom.btnAi));
  dom.btnExport.addEventListener('click', () => openExportPopover(dom.btnExport));
  dom.btnCategoryChip.addEventListener('click', () => openCategoryPopover(dom.btnCategoryChip));
  dom.btnMore.addEventListener('click', () => openMorePopover(dom.btnMore));

  dom.editorScroll.addEventListener('scroll', closePopover);

  dom.voiceStop.addEventListener('click', () => stopVoice());
  dom.recordStop.addEventListener('click', () => stopRecording());

  dom.popoverLayer.addEventListener('mousedown', (event) => {
    if (event.target === dom.popoverLayer) closePopover();
  });

  dom.modalLayer.addEventListener('mousedown', (event) => {
    if (event.target === dom.modalLayer) closeModal(false);
  });

  dom.drawClose.addEventListener('click', closeDraw);
  dom.drawCancel.addEventListener('click', closeDraw);
  dom.drawClear.addEventListener('click', initCanvas);
  dom.drawInsert.addEventListener('click', insertDrawing);
  dom.penColors.addEventListener('click', (event) => {
    const button = event.target.closest('.pen-color');
    if (!button) return;
    dom.penColors.querySelectorAll('.pen-color').forEach((b) => b.classList.remove('active'));
    button.classList.add('active');
    state.draw.color = button.dataset.color;
  });
  dom.drawModes.addEventListener('click', (event) => {
    const button = event.target.closest('.draw-mode');
    if (!button) return;
    dom.drawModes.querySelectorAll('.draw-mode').forEach((b) => b.classList.remove('active'));
    button.classList.add('active');
    state.draw.tool = button.dataset.tool;
  });
  dom.drawWidth.addEventListener('input', () => {
    state.draw.width = parseFloat(dom.drawWidth.value) || 3;
    dom.drawWidthLabel.textContent = String(state.draw.width);
  });
  bindCanvas();

  document.addEventListener('keydown', onGlobalKeyDown);
  document.addEventListener('selectionchange', () => {
    const selection = window.getSelection();
    if (selection.rangeCount && dom.body.contains(selection.anchorNode)) {
      state.lastRange = selection.getRangeAt(0).cloneRange();
    }
  });

  dom.app.addEventListener('dragover', (event) => {
    if (Array.from(event.dataTransfer?.types || []).includes('Files')) {
      event.preventDefault();
      event.dataTransfer.dropEffect = 'copy';
    }
  });
  dom.app.addEventListener('drop', onAppDrop);
}

function onGlobalKeyDown(event) {
  if (event.isComposing) return;
  const ctrl = event.ctrlKey || event.metaKey;

  if (ctrl && event.key.toLowerCase() === 'n') {
    event.preventDefault();
    newNote();
    return;
  }
  if (ctrl && event.key.toLowerCase() === 'o') {
    event.preventDefault();
    importFromDialog();
    return;
  }
  if (ctrl && event.key.toLowerCase() === 'f') {
    event.preventDefault();
    if (state.settingsOpen) {
      dom.settingsSearch.focus();
      dom.settingsSearch.select();
    } else {
      dom.searchInput.focus();
      dom.searchInput.select();
    }
    return;
  }
  if (ctrl && event.key === ',') {
    event.preventDefault();
    if (state.settingsOpen) closeSettingsPage();
    else openSettingsPage();
    return;
  }
  if (ctrl && event.key.toLowerCase() === 's') {
    event.preventDefault();
    flushSave().then((ok) => ok && toast('已保存'));
    return;
  }
  if (event.key === 'Escape') {
    if (!dom.drawLayer.classList.contains('hidden')) {
      closeDraw();
    } else if (!dom.modalLayer.classList.contains('hidden')) {
      closeModal(false);
    } else if (!dom.popoverLayer.classList.contains('hidden')) {
      closePopover();
    } else if (state.settingsOpen) {
      closeSettingsPage();
    } else if (state.voiceOn) {
      stopVoice();
    } else if (state.selectedImage) {
      deselectImage();
    }
  }
}

/* ================= 数据工具 ================= */

function normalizeNote(raw) {
  return {
    id: raw.id || uid(),
    title: raw.title || '',
    html: raw.html || '',
    text: raw.text || '',
    color: raw.color || 'default',
    category: raw.category || null,
    pinned: !!raw.pinned,
    createdAt: raw.createdAt || new Date().toISOString(),
    updatedAt: raw.updatedAt || new Date().toISOString(),
    deleted: !!raw.deleted,
    deletedAt: raw.deletedAt || null,
    images: Array.isArray(raw.images) ? raw.images.slice() : [],
    tags: Array.isArray(raw.tags) ? raw.tags.map((tag) => String(tag)).filter(Boolean) : [],
    files: Array.isArray(raw.files) ? raw.files.slice() : [],
    locked: !!raw.locked,
    lockedPayload: raw.lockedPayload || null,
  };
}

function uid() {
  return 'n' + Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
}

function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, (c) => (
    { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
  ));
}

function pad(value) {
  return String(value).padStart(2, '0');
}

function fmtDate(iso) {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const now = new Date();
  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);
  const time = `${pad(date.getHours())}:${pad(date.getMinutes())}`;

  if (date.toDateString() === now.toDateString()) return `今天 ${time}`;
  if (date.toDateString() === yesterday.toDateString()) return `昨天 ${time}`;
  if (date.getFullYear() === now.getFullYear()) {
    return `${date.getMonth() + 1}月${date.getDate()}日`;
  }
  return `${date.getFullYear()}年${date.getMonth() + 1}月${date.getDate()}日`;
}

function fmtFull(iso) {
  if (!iso) return '';
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  const now = new Date();
  const time = `${pad(date.getHours())}:${pad(date.getMinutes())}`;
  if (date.toDateString() === now.toDateString()) return `今天 ${time}`;
  if (date.getFullYear() === now.getFullYear()) {
    return `${date.getMonth() + 1}月${date.getDate()}日 ${time}`;
  }
  return `${date.getFullYear()}/${date.getMonth() + 1}/${date.getDate()} ${time}`;
}

function countWords(text) {
  return (text || '').replace(/\s+/g, '').length;
}

function extractPlainText() {
  let out = '';

  function walk(node) {
    if (node.nodeType === Node.TEXT_NODE) {
      out += node.nodeValue || '';
      return;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return;

    const tag = node.tagName;
    if (tag === 'FIGURE' || node.classList.contains('img-del')) return;
    if (tag === 'BR') {
      out += '\n';
      return;
    }

    const isBlock = /^(DIV|P|LI|UL|OL|BLOCKQUOTE|H[1-6]|TABLE|TR|TD|TH)$/.test(tag);
    if (isBlock && out && !out.endsWith('\n')) out += '\n';
    for (const child of node.childNodes) walk(child);
    if (isBlock && out && !out.endsWith('\n')) out += '\n';
  }

  walk(dom.body);
  return out.replace(/\u00A0/g, ' ').replace(/\n{3,}/g, '\n\n').trim();
}

function noteSnippet(note) {
  if (note.locked) return '解锁后查看内容';
  return (note.text || '').replace(/\s+/g, ' ').trim().slice(0, 110);
}

function folderOf(id) {
  return state.folders.find((folder) => folder.id === id) || null;
}

function catName(id) {
  const folder = folderOf(id);
  return folder ? folder.name : '';
}

function colorOf(id) {
  const color = COLORS.find((c) => c.id === id) || COLORS[0];
  const dark = document.body.dataset.theme === 'dark';
  return {
    bg: dark ? color.dark : color.light,
    fg: dark ? '#ECEDEF' : '#1A1A1C',
    swatch: dark ? color.dark : color.light,
  };
}

function viewTitle() {
  if (state.view.type === 'pinned') return '置顶';
  if (state.view.type === 'trash') return '最近删除';
  if (state.view.type === 'category') return catName(state.view.category) || '文件夹';
  if (state.view.type === 'tag') return '#' + (state.view.tag || '标签');
  if (state.view.type === 'smart') {
    const smart = SMART_VIEWS.find((view) => view.id === state.view.smart);
    return smart ? smart.name : '智能文件夹';
  }
  return '全部笔记';
}

function noteMatchesSmart(note, id) {
  const now = new Date();
  const updated = new Date(note.updatedAt || 0);
  if (id === 'today') return updated.toDateString() === now.toDateString();
  if (id === 'week') return now - updated <= 7 * 24 * 60 * 60 * 1000;
  if (id === 'images') return (note.images || []).length > 0 || /<img\b/i.test(note.html || '');
  if (id === 'todo') return /todo-item/.test(note.html || '');
  if (id === 'locked') return !!note.locked;
  if (id === 'unfiled') return !note.category;
  return true;
}

function visibleNotes() {
  let list;
  if (state.view.type === 'trash') {
    list = state.notes.filter((note) => note.deleted);
  } else {
    list = state.notes.filter((note) => !note.deleted);
    if (state.view.type === 'pinned') list = list.filter((note) => note.pinned);
    if (state.view.type === 'category') {
      list = list.filter((note) => note.category === state.view.category);
    }
    if (state.view.type === 'tag') {
      list = list.filter((note) => (note.tags || []).includes(state.view.tag));
    }
    if (state.view.type === 'smart') {
      list = list.filter((note) => noteMatchesSmart(note, state.view.smart));
    }
  }

  const query = state.query.trim().toLowerCase();
  if (query) {
    list = list.filter((note) => {
      const haystack = note.locked
        ? '已锁定的备忘录'
        : `${note.title}\n${note.text}\n${(note.tags || []).join(' ')}`;
      return haystack.toLowerCase().includes(query);
    });
  }

  if (state.view.type === 'trash') {
    list.sort((a, b) => new Date(b.deletedAt || 0) - new Date(a.deletedAt || 0));
  } else {
    list.sort((a, b) => {
      if (a.pinned !== b.pinned) return a.pinned ? -1 : 1;
      if (state.sort === 'created') {
        return new Date(b.createdAt || 0) - new Date(a.createdAt || 0);
      }
      if (state.sort === 'title') {
        return (a.title || '无标题').localeCompare(b.title || '无标题', 'zh-CN');
      }
      return new Date(b.updatedAt || 0) - new Date(a.updatedAt || 0);
    });
  }
  return list;
}

/* ================= 渲染 ================= */

function renderSidebar() {
  state.tags = collectTags();
  const alive = state.notes.filter((note) => !note.deleted);
  dom.countAll.textContent = alive.length;
  dom.countPinned.textContent = alive.filter((note) => note.pinned).length;
  dom.countTrash.textContent = state.notes.filter((note) => note.deleted).length;

  dom.folders.innerHTML = state.folders
    .map((folder) => {
      const count = alive.filter((note) => note.category === folder.id).length;
      const active = state.view.type === 'category' && state.view.category === folder.id;
      return `<button class="cat-item ${active ? 'active' : ''}" data-folder="${escapeHtml(folder.id)}" type="button">
        <i style="--dot:${escapeHtml(folder.color)}"></i>
        <span>${escapeHtml(folder.name)}</span>
        <em>${count}</em>
      </button>`;
    })
    .join('');

  dom.tagList.innerHTML = state.tags.length
    ? state.tags
        .map((tag) => {
          const active = state.view.type === 'tag' && state.view.tag === tag.name;
          return `<button class="tag-pill ${active ? 'active' : ''}" data-tag="${escapeHtml(tag.name)}" type="button" title="${escapeHtml(tag.name)} · ${tag.count} 条">
            #${escapeHtml(tag.name)}<b>${tag.count}</b>
          </button>`;
        })
        .join('')
    : '<div class="sidebar-empty">暂无标签</div>';

  dom.smartList.innerHTML = SMART_VIEWS.map((smart) => {
    const count = alive.filter((note) => noteMatchesSmart(note, smart.id)).length;
    const active = state.view.type === 'smart' && state.view.smart === smart.id;
    return `<button class="cat-item ${active ? 'active' : ''}" data-smart="${smart.id}" type="button">
      <svg class="icon smart-icon"><use href="#${smart.icon}"/></svg>
      <span>${smart.name}</span>
      <em>${count}</em>
    </button>`;
  }).join('');

  dom.nav.querySelectorAll('.nav-item').forEach((button) => {
    button.classList.toggle('active', state.view.type === button.dataset.view);
  });

  if (dom.profileLabel) {
    dom.profileLabel.textContent = state.profile?.name || '本机';
  }
}

function renderList() {
  const notes = visibleNotes();
  const trash = state.view.type === 'trash';
  dom.viewTitle.textContent = state.query.trim() ? '搜索结果' : viewTitle();
  dom.viewCount.textContent = notes.length;
  dom.btnEmptyTrash.classList.toggle('hidden', !trash || notes.length === 0);
  dom.btnSort.classList.toggle('hidden', trash);

  dom.noteList.innerHTML = notes.map((note) => cardHtml(note, trash)).join('');
  dom.noteList.classList.toggle('hidden', notes.length === 0);

  if (notes.length === 0) {
    dom.listEmpty.classList.remove('hidden');
    dom.listEmptyText.textContent = emptyText();
  } else {
    dom.listEmpty.classList.add('hidden');
  }
}

function emptyText() {
  if (state.query.trim()) return '没有找到相关备忘录';
  if (state.view.type === 'trash') return '最近删除是空的';
  if (state.view.type === 'category') return '这个文件夹里还没有笔记';
  if (state.view.type === 'tag') return '这个标签下还没有笔记';
  if (state.view.type === 'smart') return '这个智能文件夹里还没有笔记';
  if (state.view.type === 'pinned') return '还没有置顶的备忘录';
  return '还没有备忘录';
}

function cardHtml(note, trash) {
  const colors = colorOf(note.color);
  const locked = !!note.locked;
  const title = escapeHtml(locked ? '已锁定的备忘录' : (note.title.trim() || '无标题'));
  const snippet = escapeHtml(locked ? '点击解锁后查看内容' : noteSnippet(note)) || '空白备忘录';
  const pin = note.pinned
    ? '<svg class="icon pin-flag"><use href="#i-pin"/></svg>'
    : '';
  const image = !locked && note.images && note.images.length
    ? '<svg class="icon"><use href="#i-image"/></svg>'
    : '';
  const todo = !locked && /todo-item/.test(note.html || '')
    ? '<svg class="icon"><use href="#i-todo"/></svg>'
    : '';
  const lock = locked
    ? '<svg class="icon card-lock"><use href="#i-lock"/></svg>'
    : '';
  const files = !locked && note.files && note.files.length
    ? '<svg class="icon"><use href="#i-file"/></svg>'
    : '';

  const foot = trash
    ? `<span class="card-date">删除于 ${fmtDate(note.deletedAt)}</span>
       <span class="del-actions">
         <button class="mini-btn" data-act="restore" type="button">恢复</button>
         <button class="mini-btn danger" data-act="purge" type="button">彻底删除</button>
       </span>`
    : `<span class="card-date">${fmtDate(note.updatedAt)}</span>
       <span class="card-icons">${lock}${pin}${image}${files}${todo}</span>`;

  return `<article class="note-card ${trash ? 'trash' : ''} ${locked ? 'locked-card' : ''} ${note.id === state.currentId ? 'active' : ''}"
      data-id="${escapeHtml(note.id)}"
      style="--note-bg:${colors.bg};--note-fg:${colors.fg}">
    <div class="card-title">${title}</div>
    <div class="card-snippet">${snippet}</div>
    <div class="card-foot">${foot}</div>
  </article>`;
}

function updateCardInList(note) {
  if (!note || note.deleted) return;
  const card = dom.noteList.querySelector(`.note-card[data-id="${CSS.escape(note.id)}"]`);
  if (!card) return;
  const title = card.querySelector('.card-title');
  const snippet = card.querySelector('.card-snippet');
  const date = card.querySelector('.card-date');
  if (title) title.textContent = note.locked ? '已锁定的备忘录' : (note.title.trim() || '无标题');
  if (snippet) snippet.textContent = note.locked ? '点击解锁后查看内容' : (noteSnippet(note) || '空白备忘录');
  if (date) date.textContent = fmtDate(note.updatedAt);
}

function updateEmptyBody() {
  const hasText = extractPlainText().length > 0;
  const hasImage = !!dom.body.querySelector('figure');
  dom.body.classList.toggle('is-empty', !hasText && !hasImage);
}

/* ================= 打开 / 新建 / 删除 ================= */

async function openNote(id) {
  if (state.currentId === id) {
    dom.noteList.querySelectorAll('.note-card').forEach((card) => {
      card.classList.toggle('active', card.dataset.id === id);
    });
    return;
  }
  if (state.recording) await stopRecording();
  if (state.currentNote) await flushSave();
  if (state.voiceOn) await stopVoice();

  const note = state.notes.find((item) => item.id === id && !item.deleted);
  if (!note) {
    showEditorEmpty();
    return;
  }

  if (note.locked) {
    const unlocked = await unlockLockedNote(note);
    if (unlocked) {
      await openNote(note.id);
    }
    return;
  }

  state.currentId = id;
  state.currentNote = note;
  state.selectedImage = null;
  state.lastRange = null;
  state.voiceRange = null;
  state.dirty = false;

  dom.titleInput.value = note.title || '';
  dom.body.innerHTML = note.html || '';
  dom.body.querySelectorAll('.img-del').forEach((button) => {
    button.textContent = '';
  });
  updateEmptyBody();
  renderTagRow();

  dom.editor.classList.remove('hidden');
  dom.editorEmpty.classList.add('hidden');
  applyEditorColor(note);
  syncPinButton();
  syncCategoryChip();
  updateEditorMeta();

  dom.editorScroll.scrollTop = 0;
  dom.noteList.querySelectorAll('.note-card').forEach((card) => {
    card.classList.toggle('active', card.dataset.id === id);
  });
  requestAnimationFrame(() => {
    const card = dom.noteList.querySelector('.note-card.active');
    if (card) card.scrollIntoView({ block: 'nearest' });
  });
}

function showEditorEmpty() {
  state.currentId = null;
  state.currentNote = null;
  state.selectedImage = null;
  state.dirty = false;
  dom.editor.classList.add('hidden');
  dom.editorEmpty.classList.remove('hidden');
  dom.editorPane.style.background = '';
  dom.tagRow.innerHTML = '';
}

async function newNote() {
  if (state.recording) await stopRecording();
  if (state.currentNote) await flushSave();

  const now = new Date().toISOString();
  const note = {
    id: uid(),
    title: '',
    html: '',
    text: '',
    color: 'default',
    category: state.view.type === 'category' ? state.view.category : null,
    pinned: false,
    createdAt: now,
    updatedAt: now,
    deleted: false,
    deletedAt: null,
    images: [],
    tags: state.view.type === 'tag' && state.view.tag ? [state.view.tag] : [],
    files: [],
    locked: false,
    lockedPayload: null,
  };

  state.notes.unshift(note);
  if (state.view.type === 'trash') {
    state.view = { type: 'all', category: null, tag: null, smart: null };
  }
  if (state.query) {
    state.query = '';
    dom.searchInput.value = '';
    dom.btnClearSearch.classList.add('hidden');
  }

  renderSidebar();
  renderList();
  await openNote(note.id);
  dom.titleInput.focus();
  await persist();
}

/* ================= 导入 ================= */

async function importFromDialog() {
  try {
    const result = await call('importFile');
    if (!result) return;
    await createImportedNote(result);
  } catch (error) {
    toast('导入失败：' + error.message, 'error');
  }
}

async function importFromPath(path) {
  const result = await call('importPath', { path });
  if (!result) throw new Error('没有读到文件内容');
  await createImportedNote(result);
}

async function onAppDrop(event) {
  if (event.defaultPrevented) return;
  const files = Array.from(event.dataTransfer?.files || []);
  if (!files.length) return;

  event.preventDefault();
  for (const file of files) {
    try {
      const dataUrl = await readFileAsDataUrl(file);
      const result = await call('importData', { name: file.name, base64: dataUrl });
      if (result) await createImportedNote(result);
    } catch (error) {
      toast('导入 ' + file.name + ' 失败：' + error.message, 'error');
    }
  }
}

async function createImportedNote(result) {
  if (!result) return;

  let html = result.html || '';
  html = result.needsSanitize ? sanitizeImportedHtml(html) : normalizeImportedHtml(html);
  if (!html.trim()) {
    html = textToHtml(result.text || '');
  }

  const holder = document.createElement('div');
  holder.innerHTML = html;
  const images = Array.from(holder.querySelectorAll('figure[data-file]')).map(
    (figure) => figure.dataset.file
  );

  const now = new Date().toISOString();
  const title = (result.title || '').trim() || '导入的备忘录';
  const note = {
    id: uid(),
    title,
    html,
    text: (result.text || '').trim(),
    color: 'default',
    category: null,
    pinned: false,
    createdAt: now,
    updatedAt: now,
    deleted: false,
    deletedAt: null,
    images,
    tags: [],
    files: [],
    locked: false,
    lockedPayload: null,
  };

  state.notes.unshift(note);
  state.view = { type: 'all', category: null, tag: null, smart: null };
  if (state.query) {
    state.query = '';
    dom.searchInput.value = '';
    dom.btnClearSearch.classList.add('hidden');
  }

  renderSidebar();
  renderList();
  await openNote(note.id);

  note.text = extractPlainText();
  note.images = Array.from(dom.body.querySelectorAll('figure[data-file]')).map(
    (figure) => figure.dataset.file
  );
  note.files = collectAttachments();
  updateEditorMeta();
  await persist();

  toast(result.warning || `已导入「${title}」`);
}

function textToHtml(text) {
  return String(text || '')
    .split(/\r?\n/)
    .map((line) => (line.trim() ? `<div>${escapeHtml(line)}</div>` : '<div><br></div>'))
    .join('');
}

function mediaFileFromSrc(src) {
  const prefix = 'https://media.local/';
  if (!src || !src.startsWith(prefix)) return null;
  try {
    const name = decodeURIComponent(src.slice(prefix.length).split('?')[0].split('#')[0]);
    return /^[A-Za-z0-9._-]+$/.test(name) ? name : null;
  } catch {
    return null;
  }
}

function wrapMediaImages(doc, root) {
  for (const img of Array.from(root.querySelectorAll('img'))) {
    const file = mediaFileFromSrc(img.getAttribute('src') || '');
    if (!file) continue;

    img.dataset.file = file;
    let figure = img.closest('figure');
    if (!figure) {
      figure = doc.createElement('figure');
      img.replaceWith(figure);
      figure.appendChild(img);
    }

    figure.classList.add('img');
    figure.setAttribute('contenteditable', 'false');
    figure.dataset.file = file;

    if (figure.parentElement && figure.parentElement.tagName === 'P') {
      figure.parentElement.after(figure);
    }

    if (!figure.querySelector('.img-del')) {
      const button = doc.createElement('button');
      button.className = 'img-del';
      button.type = 'button';
      button.setAttribute('aria-label', '删除图片');
      figure.appendChild(button);
    }
  }

  root.querySelectorAll('figure').forEach((figure) => {
    if (!figure.querySelector('img')) figure.remove();
  });
}

function normalizeImportedHtml(html) {
  const holder = document.createElement('div');
  holder.innerHTML = html;
  wrapMediaImages(document, holder);
  return holder.innerHTML;
}

function sanitizeImportedHtml(raw) {
  const doc = new DOMParser().parseFromString(raw || '', 'text/html');
  const allowed = new Set([
    'DIV', 'P', 'BR', 'B', 'STRONG', 'I', 'EM', 'U', 'S', 'STRIKE',
    'UL', 'OL', 'LI', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'BLOCKQUOTE',
    'PRE', 'CODE', 'TABLE', 'THEAD', 'TBODY', 'TFOOT', 'TR', 'TD', 'TH',
    'A', 'IMG', 'FIGURE', 'FIGCAPTION', 'SPAN', 'HR',
  ]);
  const dropped = new Set([
    'SCRIPT', 'STYLE', 'LINK', 'META', 'IFRAME', 'OBJECT', 'EMBED', 'FORM',
    'INPUT', 'BUTTON', 'TEXTAREA', 'SELECT', 'SVG', 'CANVAS', 'VIDEO', 'AUDIO',
    'SOURCE', 'NOSCRIPT', 'TEMPLATE',
  ]);

  const walk = (node) => {
    for (const child of Array.from(node.childNodes)) {
      if (child.nodeType === Node.TEXT_NODE) continue;
      if (child.nodeType !== Node.ELEMENT_NODE) {
        child.remove();
        continue;
      }

      const tag = child.tagName.toUpperCase();
      if (dropped.has(tag)) {
        child.remove();
        continue;
      }

      walk(child);

      if (!allowed.has(tag)) {
        const fragment = doc.createDocumentFragment();
        while (child.firstChild) fragment.appendChild(child.firstChild);
        child.replaceWith(fragment);
        continue;
      }

      for (const attribute of Array.from(child.attributes)) {
        const name = attribute.name.toLowerCase();
        const value = attribute.value;

        if (tag === 'A' && name === 'href') {
          if (!/^(https?:|mailto:)/i.test(value.trim())) {
            child.removeAttribute('href');
          }
          continue;
        }

        if (tag === 'IMG' && name === 'src') {
          if (!/^(https:\/\/media\.local\/|https:\/\/|data:image\/)/i.test(value.trim())) {
            child.removeAttribute('src');
          }
          continue;
        }

        if (tag === 'IMG' && name === 'alt') continue;

        if ((tag === 'TD' || tag === 'TH') && (name === 'colspan' || name === 'rowspan')) {
          if (!/^\d{1,3}$/.test(value.trim())) child.removeAttribute(attribute.name);
          continue;
        }

        if (tag === 'FIGURE' && name === 'data-file') continue;

        if (tag === 'DIV' && name === 'data-checked') {
          if (value !== 'true' && value !== 'false') child.removeAttribute(attribute.name);
          continue;
        }

        if (name === 'class') {
          const classes = value
            .split(/\s+/)
            .filter((name) => ['todo-item', 'todo-box', 'todo-text', 'img'].includes(name));
          if (classes.length) child.setAttribute('class', classes.join(' '));
          else child.removeAttribute('class');
          continue;
        }

        child.removeAttribute(attribute.name);
      }

      if (tag === 'IMG' && !child.getAttribute('src')) {
        child.remove();
      }
    }
  };

  walk(doc.body);
  wrapMediaImages(doc, doc.body);
  return doc.body.innerHTML;
}

/* ================= 导出 ================= */

function openExportPopover(anchor) {
  if (!state.currentNote) return;

  const formats = [
    ['txt', '纯文本 (.txt)', '#i-note'],
    ['md', 'Markdown (.md)', '#i-note'],
    ['docx', 'Word 文档 (.docx)', '#i-note'],
    ['pdf', 'PDF 文档 (.pdf)', '#i-download'],
    ['html', '网页 (.html)', '#i-folder-open'],
  ];

  const popover = mountPopover(
    `<div class="pop-title">导出备忘录</div>${formats
      .map(
        ([id, label, icon]) =>
          `<button class="pop-item" type="button" data-format="${id}">
             <svg class="icon"><use href="${icon}"/></svg><span>${label}</span>
           </button>`
      )
      .join('')}`,
    anchor
  );

  popover.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-format]');
    if (!button) return;
    closePopover();
    await exportAs(button.dataset.format);
  });
}

async function exportAs(format) {
  if (!state.currentNote) return;
  await flushSave();
  const title = (state.currentNote.title || '').trim() || '无标题备忘录';

  try {
    let result;
    if (format === 'txt') {
      result = await call('exportText', { title, text: state.currentNote.text });
    } else if (format === 'md') {
      result = await call('exportMarkdown', { title, markdown: buildMarkdown() });
    } else if (format === 'html') {
      result = await call('exportHtml', { title, html: exportBodyHtml() });
    } else if (format === 'docx') {
      result = await call('exportDocx', { title, blocks: buildExportBlocks() });
    } else if (format === 'pdf') {
      result = await exportPdf();
    }

    if (result && result.path) {
      toast('已导出到 ' + result.path);
    }
  } catch (error) {
    toast('导出失败：' + error.message, 'error');
  }
}

function exportBodyHtml() {
  const clone = dom.body.cloneNode(true);
  clone.querySelectorAll('.img-del').forEach((button) => button.remove());
  clone.querySelectorAll('[contenteditable]').forEach((element) => {
    element.removeAttribute('contenteditable');
  });
  return clone.innerHTML;
}

async function exportPdf(path = null) {
  const title = (state.currentNote.title || '').trim() || '无标题备忘录';
  dom.printRoot.innerHTML =
    `<h1>${escapeHtml(title)}</h1>` +
    `<div class="print-meta">导出自 WinNote 备忘录 · ${escapeHtml(fmtFull(new Date().toISOString()))}</div>` +
    `<div class="print-content">${exportBodyHtml()}</div>`;

  await waitForImages(dom.printRoot);
  try {
    return await call('exportPdf', path ? { title, path } : { title });
  } finally {
    dom.printRoot.innerHTML = '';
  }
}

function waitForImages(root) {
  const images = Array.from(root.querySelectorAll('img'));
  return Promise.all(
    images.map(
      (image) =>
        new Promise((resolve) => {
          if (image.complete) {
            resolve();
            return;
          }
          image.onload = resolve;
          image.onerror = resolve;
          setTimeout(resolve, 4000);
        })
    )
  ).then(() => new Promise((resolve) => setTimeout(resolve, 150)));
}

function buildMarkdown() {
  const lines = [];
  const title = (state.currentNote?.title || '').trim();
  if (title) lines.push('# ' + title, '');

  const inline = (node) => {
    let output = '';
    for (const child of node.childNodes) {
      if (child.nodeType === Node.TEXT_NODE) {
        output += child.nodeValue || '';
        continue;
      }
      if (child.nodeType !== Node.ELEMENT_NODE) continue;

      const tag = child.tagName.toLowerCase();
      if (child.classList.contains('todo-box')) continue;

      if (tag === 'br') output += '  \n';
      else if (tag === 'b' || tag === 'strong') output += '**' + inline(child) + '**';
      else if (tag === 'i' || tag === 'em') output += '*' + inline(child) + '*';
      else if (tag === 'u') output += '<u>' + inline(child) + '</u>';
      else if (tag === 's' || tag === 'strike') output += '~~' + inline(child) + '~~';
      else if (tag === 'code') output += '`' + child.textContent + '`';
      else if (tag === 'a') output += `[${inline(child)}](${child.getAttribute('href') || ''})`;
      else if (tag === 'img') output += `![${child.getAttribute('alt') || ''}](${child.getAttribute('src') || ''})`;
      else output += inline(child);
    }
    return output;
  };

  const block = (node) => {
    if (node.nodeType === Node.TEXT_NODE) {
      const value = node.nodeValue.trim();
      if (value) lines.push(value);
      return;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return;

    const tag = node.tagName.toLowerCase();

    if (tag === 'figure') {
      const image = node.querySelector('img');
      if (image) {
        lines.push(`![${image.getAttribute('alt') || ''}](${image.getAttribute('src') || ''})`);
      }
      return;
    }

    if (node.classList.contains('todo-item')) {
      const text = inline(node.querySelector('.todo-text') || node).trim();
      lines.push(`- [${node.dataset.checked === 'true' ? 'x' : ' '}] ${text}`);
      return;
    }

    if (/^h[1-6]$/.test(tag)) {
      lines.push('#'.repeat(Number(tag[1])) + ' ' + inline(node).trim());
      return;
    }

    if (tag === 'ul' || tag === 'ol') {
      let number = 1;
      for (const item of node.querySelectorAll(':scope > li')) {
        lines.push((tag === 'ol' ? `${number++}. ` : '- ') + inline(item).trim());
      }
      return;
    }

    if (tag === 'blockquote') {
      lines.push(
        inline(node)
          .trim()
          .split('\n')
          .map((line) => '> ' + line)
          .join('\n')
      );
      return;
    }

    if (tag === 'pre') {
      lines.push('```\n' + node.textContent.replace(/\n$/, '') + '\n```');
      return;
    }

    if (tag === 'hr') {
      lines.push('---');
      return;
    }

    if (tag === 'table') {
      let firstRow = true;
      for (const row of node.querySelectorAll('tr')) {
        const cells = Array.from(row.children).map((cell) =>
          inline(cell).trim().replace(/\|/g, '\\|')
        );
        if (cells.length) {
          lines.push('| ' + cells.join(' | ') + ' |');
          if (firstRow) {
            lines.push('| ' + cells.map(() => '---').join(' | ') + ' |');
            firstRow = false;
          }
        }
      }
      return;
    }

    const text = inline(node).trim();
    if (text) lines.push(text);
  };

  for (const node of dom.body.childNodes) {
    block(node);
    lines.push('');
  }

  return lines.join('\n').replace(/\n{3,}/g, '\n\n').trim() + '\n';
}

function buildExportBlocks() {
  const blocks = [];

  const inlineRuns = (node, base = { bold: false, italic: false, underline: false }) => {
    const runs = [];

    const push = (text, style) => {
      if (!text) return;
      const last = runs[runs.length - 1];
      if (last
          && last.bold === style.bold
          && last.italic === style.italic
          && last.underline === style.underline) {
        last.text += text;
      } else {
        runs.push({ text, ...style });
      }
    };

    const walk = (element, style) => {
      for (const child of element.childNodes) {
        if (child.nodeType === Node.TEXT_NODE) {
          push(child.nodeValue || '', style);
          continue;
        }
        if (child.nodeType !== Node.ELEMENT_NODE) continue;

        const tag = child.tagName.toLowerCase();
        if (child.classList.contains('todo-box')) continue;

        if (tag === 'br') push('\n', style);
        else if (tag === 'b' || tag === 'strong') walk(child, { ...style, bold: true });
        else if (tag === 'i' || tag === 'em') walk(child, { ...style, italic: true });
        else if (tag === 'u') walk(child, { ...style, underline: true });
        else if (tag === 'img') continue;
        else walk(child, style);
      }
    };

    walk(node, base);
    return runs.filter((run) => run.text.length > 0);
  };

  for (const node of dom.body.childNodes) {
    if (node.nodeType === Node.TEXT_NODE) {
      const text = node.nodeValue.trim();
      if (text) blocks.push({ type: 'p', runs: [{ text }] });
      continue;
    }
    if (node.nodeType !== Node.ELEMENT_NODE) continue;

    const tag = node.tagName.toLowerCase();

    if (tag === 'figure') {
      const file = node.dataset.file || node.querySelector('img')?.dataset.file;
      if (file) blocks.push({ type: 'image', file });
      continue;
    }

    if (node.classList.contains('todo-item')) {
      blocks.push({
        type: 'todo',
        checked: node.dataset.checked === 'true',
        runs: inlineRuns(node.querySelector('.todo-text') || node),
      });
      continue;
    }

    if (/^h[1-6]$/.test(tag)) {
      blocks.push({ type: 'h2', runs: inlineRuns(node) });
      continue;
    }

    if (tag === 'ul' || tag === 'ol') {
      for (const item of node.querySelectorAll(':scope > li')) {
        blocks.push({ type: 'bullet', runs: inlineRuns(item) });
      }
      continue;
    }

    if (tag === 'blockquote') {
      blocks.push({ type: 'quote', runs: inlineRuns(node) });
      continue;
    }

    if (tag === 'pre') {
      blocks.push({ type: 'p', runs: [{ text: node.textContent }] });
      continue;
    }

    if (tag === 'table') {
      for (const row of node.querySelectorAll('tr')) {
        const cells = Array.from(row.children)
          .map((cell) => cell.innerText.trim())
          .filter(Boolean);
        if (cells.length) blocks.push({ type: 'p', runs: [{ text: cells.join(' ｜ ') }] });
      }
      continue;
    }

    const runs = inlineRuns(node);
    if (runs.length) blocks.push({ type: 'p', runs });
  }

  return blocks;
}

async function onNoteListClick(event) {
  const actionButton = event.target.closest('[data-act]');
  const card = event.target.closest('.note-card');
  if (!card) return;

  const note = state.notes.find((item) => item.id === card.dataset.id);
  if (!note) return;

  if (actionButton) {
    event.stopPropagation();
    if (actionButton.dataset.act === 'restore') await restoreNote(note);
    else if (actionButton.dataset.act === 'purge') await purgeNote(note);
    return;
  }

  if (note.deleted) return;
  await openNote(note.id);
}

async function trashNote(note) {
  if (!note) return;
  if (state.currentId === note.id) await flushSave();
  note.deleted = true;
  note.deletedAt = new Date().toISOString();
  await persist();

  if (state.currentId === note.id) showEditorEmpty();
  renderSidebar();
  renderList();

  toast('已移到「最近删除」', 'info', {
    label: '撤销',
    onClick: () => restoreNote(note),
  });
}

async function restoreNote(note) {
  note.deleted = false;
  note.deletedAt = null;
  await persist();
  renderSidebar();
  renderList();
  toast('已恢复');
}

async function purgeNote(note) {
  const title = note.title.trim() || '无标题';
  const ok = await confirmDialog(
    '彻底删除',
    `「${title}」将被永久删除，无法恢复。`,
    '彻底删除',
    true
  );
  if (!ok) return;

  state.notes = state.notes.filter((item) => item.id !== note.id);
  if (state.currentId === note.id) showEditorEmpty();
  await persist();
  renderSidebar();
  renderList();
  toast('已彻底删除');
}

async function emptyTrash() {
  const count = state.notes.filter((note) => note.deleted).length;
  if (!count) return;
  const ok = await confirmDialog(
    '清空最近删除',
    `${count} 条备忘录将被永久删除，无法恢复。`,
    '清空',
    true
  );
  if (!ok) return;

  state.notes = state.notes.filter((note) => !note.deleted);
  if (state.currentNote && state.currentNote.deleted) showEditorEmpty();
  await persist();
  renderSidebar();
  renderList();
  toast('已清空');
}

async function setView(type, category = null, tag = null, smart = null) {
  if (state.recording) await stopRecording();
  if (state.currentNote) await flushSave();
  state.view = { type, category, tag, smart };
  renderSidebar();
  renderList();

  const visible = visibleNotes();
  if (state.currentNote && !visible.some((note) => note.id === state.currentId)) {
    showEditorEmpty();
  }
}

/* ================= 保存 ================= */

function collectAttachments() {
  return Array.from(dom.body.querySelectorAll('[data-file]'))
    .filter((element) => element.tagName !== 'IMG' && !element.closest('figure.img'))
    .map((element) => element.dataset.file)
    .filter(Boolean);
}

function collectCurrent() {
  const note = state.currentNote;
  if (!note || note.locked) return;
  note.title = dom.titleInput.value;
  note.html = dom.body.innerHTML;
  note.text = extractPlainText();
  note.images = Array.from(dom.body.querySelectorAll('figure[data-file]')).map(
    (figure) => figure.dataset.file
  );
  note.files = collectAttachments();
  note.updatedAt = new Date().toISOString();
}

async function persist() {
  try {
    await call('saveNotes', state.notes);
    return true;
  } catch (error) {
    toast('保存失败：' + error.message, 'error');
    return false;
  }
}

async function flushSave() {
  if (state.saveTimer) {
    clearTimeout(state.saveTimer);
    state.saveTimer = null;
  }
  if (!state.currentNote || !state.dirty) return true;

  collectCurrent();
  state.dirty = false;
  dom.saveState.textContent = '保存中…';

  const ok = await persist();
  if (ok) {
    dom.saveState.textContent = '已保存';
    dom.noteTime.textContent = '编辑于 ' + fmtFull(state.currentNote.updatedAt);
    dom.noteWords.textContent = `${countWords(state.currentNote.text)} 字`;
    updateCardInList(state.currentNote);
    return true;
  }

  state.dirty = true;
  dom.saveState.textContent = '未保存';
  return false;
}

function scheduleSave() {
  if (!state.currentNote) return;
  state.dirty = true;
  dom.saveState.textContent = '编辑中…';
  dom.noteWords.textContent = `${countWords(extractPlainText())} 字`;
  if (state.saveTimer) clearTimeout(state.saveTimer);
  state.saveTimer = setTimeout(() => {
    state.saveTimer = null;
    flushSave();
  }, 550);
}

function updateEditorMeta() {
  const note = state.currentNote;
  if (!note) return;
  dom.noteTime.textContent = '编辑于 ' + fmtFull(note.updatedAt);
  dom.noteWords.textContent = `${countWords(note.text)} 字`;
  dom.saveState.textContent = state.dirty ? '编辑中…' : '已保存';
}

function applyEditorColor(note) {
  const colors = colorOf(note.color);
  dom.editor.style.setProperty('--note-bg', colors.bg);
  dom.editor.style.setProperty('--note-fg', colors.fg);
  dom.editorPane.style.background = document.body.dataset.style === 'glass' ? 'transparent' : colors.bg;
}

function syncPinButton() {
  const pinned = !!state.currentNote?.pinned;
  dom.btnPin.classList.toggle('active', pinned);
  dom.btnPin.title = pinned ? '取消置顶' : '置顶';
}

function syncCategoryChip() {
  const folder = folderOf(state.currentNote?.category);
  dom.catChipText.textContent = folder ? folder.name : '未分类';
  dom.catChipDot.style.background = folder ? folder.color : 'var(--text-3)';
}

async function togglePin() {
  if (!state.currentNote) return;
  await flushSave();
  state.currentNote.pinned = !state.currentNote.pinned;
  await persist();
  syncPinButton();
  renderSidebar();
  renderList();
  toast(state.currentNote.pinned ? '已置顶' : '已取消置顶');
}

async function setNoteColor(colorId) {
  if (!state.currentNote) return;
  await flushSave();
  state.currentNote.color = colorId;
  await persist();
  applyEditorColor(state.currentNote);
  renderList();
}

async function setNoteCategory(categoryId) {
  if (!state.currentNote) return;
  await flushSave();
  state.currentNote.category = categoryId || null;
  await persist();
  syncCategoryChip();
  renderSidebar();
  renderList();
  toast('已移动到「' + (catName(categoryId) || '未分类') + '」');
}

/* ================= 弹层 ================= */

function closePopover() {
  dom.popoverLayer.classList.add('hidden');
  dom.popoverLayer.innerHTML = '';
}

function positionPopover(popover, anchor) {
  const rect = anchor.getBoundingClientRect();
  const size = popover.getBoundingClientRect();
  let left = rect.left + rect.width / 2 - size.width / 2;
  left = Math.max(8, Math.min(window.innerWidth - size.width - 8, left));
  let top = rect.bottom + 8;
  if (top + size.height > window.innerHeight - 8) {
    top = Math.max(8, rect.top - size.height - 8);
  }
  popover.style.left = `${left}px`;
  popover.style.top = `${top}px`;
}

function mountPopover(html, anchor) {
  closePopover();
  const popover = document.createElement('div');
  popover.className = 'popover';
  popover.innerHTML = html;
  dom.popoverLayer.classList.remove('hidden');
  dom.popoverLayer.appendChild(popover);
  positionPopover(popover, anchor);
  return popover;
}

function openColorPopover(anchor) {
  if (!state.currentNote) return;
  const dark = document.body.dataset.theme === 'dark';
  const swatches = COLORS.map((color) => {
    const bg = dark ? color.dark : color.light;
    const active = state.currentNote.color === color.id ? 'active' : '';
    return `<button class="swatch ${active}" type="button" data-color="${color.id}"
      style="background:${bg}" title="${color.name}"></button>`;
  }).join('');

  const popover = mountPopover(
    `<div class="pop-title">笔记颜色</div><div class="swatches">${swatches}</div>`,
    anchor
  );
  popover.addEventListener('click', async (event) => {
    const button = event.target.closest('.swatch');
    if (!button) return;
    closePopover();
    await setNoteColor(button.dataset.color);
  });
}

function openCategoryPopover(anchor) {
  if (!state.currentNote) return;
  const current = state.currentNote.category || '';
  const items = state.folders.map((folder) => `
    <button class="pop-item" type="button" data-cat="${escapeHtml(folder.id)}">
      <i style="width:8px;height:8px;border-radius:50%;background:${escapeHtml(folder.color)}"></i>
      <span>${escapeHtml(folder.name)}</span>
      ${current === folder.id ? '<svg class="icon check"><use href="#i-check"/></svg>' : ''}
    </button>`).join('');

  const popover = mountPopover(
    `<div class="pop-title">移动到文件夹</div>${items}
     <div class="pop-sep"></div>
     <button class="pop-item" type="button" data-cat="">
       <svg class="icon"><use href="#i-note"/></svg><span>未分类</span>
       ${current === '' ? '<svg class="icon check"><use href="#i-check"/></svg>' : ''}
     </button>`,
    anchor
  );

  popover.addEventListener('click', async (event) => {
    const button = event.target.closest('.pop-item');
    if (!button) return;
    closePopover();
    await setNoteCategory(button.dataset.cat || null);
  });
}

function openMorePopover(anchor) {
  if (!state.currentNote) return;
  const popover = mountPopover(
    `<button class="pop-item" type="button" data-more="export">
       <svg class="icon"><use href="#i-download"/></svg><span>导出 / 分享…</span>
     </button>
     <button class="pop-item" type="button" data-more="import">
       <svg class="icon"><use href="#i-import"/></svg><span>导入文件…</span>
     </button>
     <button class="pop-item" type="button" data-more="copy">
       <svg class="icon"><use href="#i-copy"/></svg><span>复制全文</span>
     </button>
     <button class="pop-item" type="button" data-more="lock">
       <svg class="icon"><use href="#i-lock"/></svg><span>锁定备忘录</span>
     </button>
     <button class="pop-item" type="button" data-more="folder">
       <svg class="icon"><use href="#i-folder-open"/></svg><span>数据文件夹</span>
     </button>
     <div class="pop-sep"></div>
     <button class="pop-item danger" type="button" data-more="delete">
       <svg class="icon"><use href="#i-trash"/></svg><span>删除备忘录</span>
     </button>`,
    anchor
  );

  popover.addEventListener('click', async (event) => {
    const button = event.target.closest('.pop-item');
    if (!button) return;
    const action = button.dataset.more;
    closePopover();

    if (action === 'export') openExportPopover(dom.btnMore);
    else if (action === 'import') await importFromDialog();
    else if (action === 'copy') await copyFullText();
    else if (action === 'lock') await lockCurrentNote();
    else if (action === 'folder') {
      try {
        await call('openDataFolder');
      } catch (error) {
        toast(error.message, 'error');
      }
    } else if (action === 'delete') {
      if (state.currentNote) await trashNote(state.currentNote);
    }
  });
}

/* ================= 2.0：文件夹 / 标签 / 锁 / 插入 / 本地智能 ================= */

function showModal(html) {
  dom.modalCard.innerHTML = html;
  dom.modalLayer.classList.remove('hidden');
}

function promptFields({ title, message = '', fields, okText = '确定', danger = false }) {
  return new Promise((resolve) => {
    state.modalResolve = resolve;
    const fieldsHtml = fields
      .map(
        (field, index) => `
        <div class="field-stack">
          <label for="modalField${index}">${escapeHtml(field.label)}</label>
          <input id="modalField${index}" data-field="${escapeHtml(field.name)}"
                 type="${field.type || 'text'}" autocomplete="off" spellcheck="false">
        </div>`
      )
      .join('');

    showModal(`
      <h3>${escapeHtml(title)}</h3>
      ${message ? `<p>${escapeHtml(message)}</p>` : ''}
      ${fieldsHtml}
      <div class="modal-error" id="modalError"></div>
      <div class="modal-actions">
        <button class="btn ghost" type="button" data-result="cancel">取消</button>
        <button class="btn ${danger ? 'danger' : 'primary'}" type="button" data-result="ok">${escapeHtml(okText)}</button>
      </div>`);

    const inputs = Array.from(dom.modalCard.querySelectorAll('input[data-field]'));
    const error = dom.modalCard.querySelector('#modalError');

    const submit = () => {
      const values = {};
      let messageText = '';
      for (const input of inputs) {
        const field = fields.find((item) => item.name === input.dataset.field);
        const value = input.value;
        values[field.name] = value;
        if (!messageText && field.required && !value.trim()) {
          messageText = field.label + '不能为空';
        }
        if (!messageText && field.min && value.length < field.min) {
          messageText = field.label + '至少需要 ' + field.min + ' 位';
        }
      }

      if (!messageText && values.confirmPasscode !== undefined && values.confirmPasscode !== values.newPasscode) {
        messageText = '两次输入的密码不一致';
      }

      if (messageText) {
        error.textContent = messageText;
        return;
      }

      closeModal(values);
    };

    dom.modalCard.querySelector('[data-result="cancel"]').addEventListener('click', () => closeModal(false));
    dom.modalCard.querySelector('[data-result="ok"]').addEventListener('click', submit);
    dom.modalCard.querySelectorAll('input').forEach((input) => {
      input.addEventListener('keydown', (event) => {
        if (event.key === 'Enter') {
          event.preventDefault();
          submit();
        }
      });
    });
    if (inputs[0]) inputs[0].focus();
  });
}

function replaceNote(updated) {
  const note = normalizeNote(updated);
  const index = state.notes.findIndex((item) => item.id === note.id);
  if (index >= 0) state.notes[index] = note;
  else state.notes.unshift(note);
  if (state.currentId === note.id) state.currentNote = note;
  return note;
}

async function askSetPasscode() {
  return promptFields({
    title: '设置备忘录密码',
    message: '密码只保存在本机，用于锁定笔记；忘记后无法恢复，请牢记。',
    fields: [
      { name: 'newPasscode', label: '新密码（至少 4 位）', type: 'password', required: true, min: 4 },
      { name: 'confirmPasscode', label: '再输一次', type: 'password', required: true },
    ],
    okText: '设置',
  });
}

async function lockCurrentNote() {
  if (!state.currentNote) return;
  if (state.currentNote.locked) return;

  await flushSave();
  let passcode;

  try {
    if (!state.settings.hasPasscode) {
      const values = await askSetPasscode();
      if (!values) return;
      await call('setPasscode', { newPasscode: values.newPasscode });
      state.settings.hasPasscode = true;
      passcode = values.newPasscode;
    } else {
      const values = await promptFields({
        title: '锁定备忘录',
        message: '锁定后标题和正文都不会出现在列表里，需要密码才能查看。',
        fields: [{ name: 'passcode', label: '备忘录密码', type: 'password', required: true }],
        okText: '锁定',
      });
      if (!values) return;
      passcode = values.passcode;
    }

    const updated = await call('lockNote', {
      noteId: state.currentNote.id,
      passcode,
    });
    replaceNote(updated);
    showEditorEmpty();
    renderSidebar();
    renderList();
    toast('已锁定，列表里只显示“已锁定的备忘录”');
  } catch (error) {
    toast('锁定失败：' + error.message, 'error');
  }
}

async function unlockLockedNote(note) {
  if (!state.settings.hasPasscode) {
    toast('还没有设置备忘录密码', 'error');
    return false;
  }

  const values = await promptFields({
    title: '解锁备忘录',
    message: '输入备忘录密码后查看内容。',
    fields: [{ name: 'passcode', label: '备忘录密码', type: 'password', required: true }],
    okText: '解锁',
  });
  if (!values) return false;

  try {
    const updated = await call('unlockNote', {
      noteId: note.id,
      passcode: values.passcode,
    });
    replaceNote(updated);
    renderSidebar();
    renderList();
    toast('已解锁');
    return true;
  } catch (error) {
    toast('解锁失败：' + error.message, 'error');
    return false;
  }
}

async function changePasscodeFlow() {
  const fields = [];
  if (state.settings.hasPasscode) {
    fields.push({ name: 'oldPasscode', label: '原密码', type: 'password', required: true });
  }
  fields.push({ name: 'newPasscode', label: '新密码（至少 4 位）', type: 'password', required: true, min: 4 });
  fields.push({ name: 'confirmPasscode', label: '再输一次', type: 'password', required: true });

  const values = await promptFields({
    title: state.settings.hasPasscode ? '更改备忘录密码' : '设置备忘录密码',
    message: state.settings.hasPasscode
      ? '如果已有锁定的备忘录，请先解锁全部笔记再修改密码。'
      : '密码只保存在本机，忘记后无法恢复。',
    fields,
    okText: '保存',
  });
  if (!values) return;

  try {
    await call('setPasscode', {
      oldPasscode: values.oldPasscode || '',
      newPasscode: values.newPasscode,
    });
    state.settings.hasPasscode = true;
    toast('备忘录密码已保存');
  } catch (error) {
    toast('保存密码失败：' + error.message, 'error');
  }
}

function openSettings() {
  const folderCount = state.folders.length;
  const lockedCount = state.notes.filter((note) => note.locked && !note.deleted).length;
  const profiles = state.profile?.profiles || [];
  const ocrLanguages = state.settings.ocrLanguages || [];
  const ocrOptions = ocrLanguages.length
    ? ocrLanguages
        .map(
          (language) =>
            `<option value="${escapeHtml(language.tag)}" ${language.tag === state.settings.ocrLanguage ? 'selected' : ''}>${escapeHtml(language.name)}</option>`
        )
        .join('')
    : `<option value="zh-Hans" selected>系统默认</option>`;
  const profileRows = profiles
    .map((profile) => {
      const active = profile.id === state.profile?.id;
      return `<div class="profile-item ${active ? 'active' : ''}">
        <div class="profile-main">
          <div class="profile-name">${escapeHtml(profile.name)}${active ? ' · 当前' : ''}</div>
          <div class="profile-path" title="${escapeHtml(profile.path)}">${escapeHtml(profile.path)}</div>
        </div>
        ${active ? '' : `<button class="mini-btn" type="button" data-switch-profile="${escapeHtml(profile.id)}">切换</button>`}
      </div>`;
    })
    .join('');

  showModal(`
    <h3>设置</h3>
    <div class="settings-block">
      <div>
        <div class="settings-title">备忘录密码</div>
        <div class="settings-sub">${state.settings.hasPasscode ? '已设置 · ' + lockedCount + ' 条笔记已锁定' : '未设置 · 可锁定单条笔记'}</div>
      </div>
      <button class="btn ghost" type="button" id="settingsPasscode">${state.settings.hasPasscode ? '更改密码' : '设置密码'}</button>
    </div>
    <div class="settings-block">
      <div>
        <div class="settings-title">外观</div>
        <div class="settings-sub">${state.settings.theme === 'dark' ? '深色模式' : '浅色模式'}</div>
      </div>
      <button class="btn ghost" type="button" id="settingsTheme">切换</button>
    </div>
    <div class="settings-block">
      <div>
        <div class="settings-title">文字识别语言</div>
        <div class="settings-sub">笔记正文支持中文、英文、法文、俄文、阿拉伯文等任意文字；这里只影响图片 OCR，语言包由 Windows 提供。</div>
      </div>
      <select class="settings-select" id="settingsOcr">${ocrOptions}</select>
    </div>
    <div class="settings-block" style="align-items:flex-start">
      <div style="min-width:0;flex:1">
        <div class="settings-title">数据文件夹</div>
        <div class="settings-sub">不同文件夹互不影响；切换时旧文件夹原样保留，不会移动或删除。</div>
        <div class="profile-list">${profileRows}</div>
      </div>
    </div>
    <div class="profile-actions">
      <button class="mini-btn" type="button" id="settingsNewProfile">新建数据文件夹</button>
      <button class="mini-btn" type="button" id="settingsChooseFolder">选择已有文件夹</button>
      <button class="mini-btn" type="button" id="settingsRenameProfile">重命名当前</button>
    </div>
    <div class="settings-block">
      <div>
        <div class="settings-title">文件夹</div>
        <div class="settings-sub">${folderCount} 个文件夹</div>
      </div>
      <button class="btn ghost" type="button" id="settingsFolders">管理</button>
    </div>
    <div class="settings-block">
      <div>
        <div class="settings-title">快捷键</div>
        <div class="settings-sub">Ctrl+N 新建 · Ctrl+O 导入 · Ctrl+F 搜索 · Ctrl+Alt+N 快速新建</div>
      </div>
    </div>
    <div class="settings-block">
      <div>
        <div class="settings-title">数据</div>
        <div class="settings-sub">当前：${escapeHtml(state.profile?.name || '本机')}；全部保存在本机，不联网；本地智能工具也不会上传内容。</div>
      </div>
      <button class="btn ghost" type="button" id="settingsData">打开</button>
    </div>
    <div class="settings-block">
      <div>
        <div class="settings-title">版本</div>
        <div class="settings-sub">WinNote 2.0.0</div>
      </div>
    </div>
    <div class="modal-actions">
      <button class="btn primary" type="button" id="settingsClose">完成</button>
    </div>`);

  dom.modalCard.querySelector('#settingsPasscode').addEventListener('click', async () => {
    closeModal(false);
    await changePasscodeFlow();
  });
  dom.modalCard.querySelector('#settingsTheme').addEventListener('click', () => {
    closeModal(false);
    applyTheme(state.settings.theme === 'dark' ? 'light' : 'dark', true);
  });
  dom.modalCard.querySelector('#settingsOcr').addEventListener('change', async (event) => {
    const language = event.target.value;
    try {
      await call('setOcrLanguage', { language });
      state.settings.ocrLanguage = language;
      toast('文字识别语言已更新');
    } catch (error) {
      toast('保存失败：' + error.message, 'error');
    }
  });
  dom.modalCard.querySelector('#settingsFolders').addEventListener('click', () => {
    closeModal(false);
    openFolderManager();
  });
  dom.modalCard.querySelector('#settingsData').addEventListener('click', () => {
    call('openDataFolder').catch((error) => toast(error.message, 'error'));
  });
  dom.modalCard.querySelector('#settingsNewProfile').addEventListener('click', createDataProfile);
  dom.modalCard.querySelector('#settingsChooseFolder').addEventListener('click', chooseDataProfile);
  dom.modalCard.querySelector('#settingsRenameProfile').addEventListener('click', renameCurrentProfile);
  dom.modalCard.querySelectorAll('[data-switch-profile]').forEach((button) => {
    button.addEventListener('click', () => switchProfile(button.dataset.switchProfile));
  });
  dom.modalCard.querySelector('#settingsClose').addEventListener('click', () => closeModal(false));
}

async function applyProfileData(data) {
  if (!data) return;
  state.notes = (data.notes || []).map(normalizeNote);
  state.settings = Object.assign(
    {
      theme: 'light', hasPasscode: false, folders: [], ocrLanguage: 'zh-Hans', ocrLanguages: [],
      appearance: {}, appearanceDir: '', customThemes: [],
    },
    data.settings || {}
  );
  state.profile = data.profile || state.profile;
  state.folders = normalizeFolders(state.settings.folders);
  state.appearance = normalizeAppearance(state.settings.appearance);
  state.settings.theme = state.appearance.mode;
  state.customThemeFiles = state.settings.customThemes || [];
  await loadCustomThemes(state.customThemeFiles);
  state.tags = collectTags();
  state.currentId = null;
  state.currentNote = null;
  state.dirty = false;
  state.query = '';
  state.view = { type: 'all', category: null, tag: null, smart: null };
  dom.searchInput.value = '';
  dom.btnClearSearch.classList.add('hidden');
  applyAppearance({ persist: false, syncChrome: true });
  renderSidebar();
  renderList();
  if (state.settingsOpen) renderSettingsPage();

  const first = visibleNotes().find((note) => !note.locked);
  if (first) await openNote(first.id);
  else showEditorEmpty();
}

async function switchProfile(id) {
  if (!id || id === state.profile?.id) return;
  if (state.recording) await stopRecording();
  await flushSave();
  try {
    const data = await call('switchProfile', { id });
    closeModal(false);
    await applyProfileData(data);
    toast('已切换到「' + (state.profile?.name || '数据文件夹') + '」');
  } catch (error) {
    toast('切换失败：' + error.message, 'error');
  }
}

async function createDataProfile() {
  const values = await promptFields({
    title: '新建数据文件夹',
    message: '会创建一个独立的数据文件夹；旧数据文件夹不会被改动。',
    fields: [{ name: 'name', label: '名称（例如：私人、公开、工作）', required: true }],
    okText: '创建',
  });
  if (!values) return;

  try {
    const data = await call('createProfile', { name: values.name });
    closeModal(false);
    await applyProfileData(data);
    toast('已创建「' + (state.profile?.name || '新数据文件夹') + '」');
  } catch (error) {
    toast('创建失败：' + error.message, 'error');
  }
}

async function chooseDataProfile() {
  try {
    const data = await call('chooseDataFolder');
    if (!data) return;
    closeModal(false);
    await applyProfileData(data);
    toast('已切换到所选数据文件夹');
  } catch (error) {
    toast('选择失败：' + error.message, 'error');
  }
}

async function renameCurrentProfile() {
  const current = state.profile?.name || '';
  const values = await promptFields({
    title: '重命名数据文件夹',
    message: '只改显示名称，不会改磁盘上的文件夹路径。',
    fields: [{ name: 'name', label: '新名称', required: true }],
    okText: '保存',
  });
  if (!values) return;

  try {
    const result = await call('renameProfile', {
      id: state.profile.id,
      name: values.name,
    });
    state.profile = result.profile;
    closeModal(false);
    renderSidebar();
    if (state.settingsOpen) renderSettingsPage();
    toast('已重命名');
  } catch (error) {
    toast('重命名失败：' + error.message, 'error');
  }
}

function openFolderManager() {
  const draft = state.folders.map((folder) => ({ ...folder }));
  const rowHtml = (folder) => `
    <div class="folder-edit-row" data-id="${escapeHtml(folder.id || '')}">
      <input type="color" value="${escapeHtml(folder.color || '#4C8DFF')}" title="颜色">
      <input type="text" value="${escapeHtml(folder.name || '')}" placeholder="文件夹名称" maxlength="24">
      <button class="mini-btn danger" type="button" data-remove-folder>删除</button>
    </div>`;

  showModal(`
    <h3>管理文件夹</h3>
    <p>可以改名、换颜色、删除；删除后里面的笔记会回到「未分类」。</p>
    <div class="folder-edit-list" id="folderEditList">${draft.map(rowHtml).join('')}</div>
    <div class="modal-error" id="folderError"></div>
    <div class="modal-actions" style="justify-content:space-between">
      <button class="btn ghost" type="button" id="folderAdd">＋ 添加文件夹</button>
      <span style="display:flex;gap:8px">
        <button class="btn ghost" type="button" id="folderCancel">取消</button>
        <button class="btn primary" type="button" id="folderSave">保存</button>
      </span>
    </div>`);

  const list = dom.modalCard.querySelector('#folderEditList');
  const error = dom.modalCard.querySelector('#folderError');

  dom.modalCard.querySelector('#folderAdd').addEventListener('click', () => {
    list.insertAdjacentHTML('beforeend', rowHtml({ id: '', name: '新文件夹', color: '#4C8DFF' }));
  });

  list.addEventListener('click', (event) => {
    const button = event.target.closest('[data-remove-folder]');
    if (!button) return;
    const row = button.closest('.folder-edit-row');
    if (row) row.remove();
  });

  dom.modalCard.querySelector('#folderCancel').addEventListener('click', () => closeModal(false));
  dom.modalCard.querySelector('#folderSave').addEventListener('click', async () => {
    const rows = Array.from(list.querySelectorAll('.folder-edit-row'));
    const folders = rows
      .map((row) => {
        const name = row.querySelector('input[type="text"]').value.trim();
        const color = row.querySelector('input[type="color"]').value;
        return {
          id: row.dataset.id || `f_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 6)}`,
          name,
          color,
        };
      })
      .filter((folder) => folder.name);

    if (!folders.length) {
      error.textContent = '至少保留一个文件夹';
      return;
    }

    const removed = state.folders
      .filter((folder) => !folders.some((item) => item.id === folder.id))
      .map((folder) => folder.id);

    try {
      const result = await call('saveFolders', folders);
      state.folders = normalizeFolders(result.folders);
      if (removed.length) {
        for (const note of state.notes) {
          if (removed.includes(note.category)) note.category = null;
        }
        await persist();
      }
      closeModal(false);
      renderSidebar();
      renderList();
      if (state.currentNote) syncCategoryChip();
      toast('文件夹已更新');
    } catch (errorObject) {
      error.textContent = errorObject.message;
    }
  });
}

function openSortPopover(anchor) {
  const options = [
    ['updated', '按修改时间'],
    ['created', '按创建时间'],
    ['title', '按标题'],
  ];
  const popover = mountPopover(
    `<div class="pop-title">排序方式</div>${options
      .map(
        ([id, label]) => `<button class="pop-item" type="button" data-sort="${id}">
          <span>${label}</span>
          ${state.sort === id ? '<svg class="icon check"><use href="#i-check"/></svg>' : ''}
        </button>`
      )
      .join('')}`,
    anchor
  );
  popover.addEventListener('click', (event) => {
    const button = event.target.closest('[data-sort]');
    if (!button) return;
    state.sort = button.dataset.sort;
    closePopover();
    renderList();
  });
}

function renderTagRow() {
  const note = state.currentNote;
  if (!note || note.locked) {
    dom.tagRow.innerHTML = '';
    return;
  }

  const tags = note.tags || [];
  dom.tagRow.innerHTML =
    tags
      .map(
        (tag) => `<span class="tag-chip">${escapeHtml(tag)}
          <button type="button" data-remove-tag="${escapeHtml(tag)}" title="移除标签">×</button>
        </span>`
      )
      .join('') +
    `<button class="tag-add" id="btnAddTag" type="button">
       <svg class="icon"><use href="#i-plus"/></svg>标签
     </button>`;
}

function addTag(name) {
  if (!state.currentNote) return;
  const tag = String(name || '').trim().replace(/^#/, '').slice(0, 24);
  if (!tag) return;
  if ((state.currentNote.tags || []).includes(tag)) {
    toast('已经有这个标签了');
    return;
  }

  state.currentNote.tags = [...(state.currentNote.tags || []), tag];
  scheduleSave();
  renderTagRow();
  renderSidebar();
  toast('已添加标签 #' + tag);
}

async function removeTag(name) {
  if (!state.currentNote) return;
  state.currentNote.tags = (state.currentNote.tags || []).filter((tag) => tag !== name);
  scheduleSave();
  renderTagRow();
  renderSidebar();
  toast('已移除标签 #' + name);
}

function openTagPopover(anchor) {
  if (!state.currentNote) return;
  const current = state.currentNote.tags || [];
  const existing = state.tags.filter((tag) => !current.includes(tag.name));
  const popover = mountPopover(
    `<div class="pop-title">添加标签</div>
     <div class="tag-pop-list">
       ${existing.length
         ? existing
             .map(
               (tag) => `<button class="pop-item" type="button" data-add-tag="${escapeHtml(tag.name)}">
                 <span class="tag-hash">#</span><span>${escapeHtml(tag.name)}</span>
               </button>`
             )
             .join('')
         : '<div class="sidebar-empty">还没有其它标签</div>'}
     </div>
     <div class="pop-sep"></div>
     <div class="tag-new">
       <input id="newTagInput" type="text" placeholder="新标签" maxlength="24" autocomplete="off">
       <button class="btn primary" type="button" id="newTagAdd">添加</button>
     </div>`,
    anchor
  );

  const input = popover.querySelector('#newTagInput');
  const submit = () => {
    const value = input.value.trim();
    if (!value) return;
    closePopover();
    addTag(value);
  };

  popover.addEventListener('click', (event) => {
    const button = event.target.closest('[data-add-tag]');
    if (!button) return;
    closePopover();
    addTag(button.dataset.addTag);
  });
  popover.querySelector('#newTagAdd').addEventListener('click', submit);
  input.addEventListener('keydown', (event) => {
    if (event.key === 'Enter') {
      event.preventDefault();
      submit();
    }
  });
  input.focus();
}

function openInsertPopover(anchor) {
  const popover = mountPopover(
    `<div class="pop-title">插入</div>
     <button class="pop-item" type="button" data-insert="table">
       <svg class="icon"><use href="#i-table"/></svg><span>表格</span>
     </button>
     <button class="pop-item" type="button" data-insert="link">
       <svg class="icon"><use href="#i-link"/></svg><span>链接</span>
     </button>
     <button class="pop-item" type="button" data-insert="file">
       <svg class="icon"><use href="#i-file"/></svg><span>文件 / 音频 / 视频</span>
     </button>
     <button class="pop-item" type="button" data-insert="record">
       <svg class="icon"><use href="#i-record"/></svg><span>录音</span>
     </button>`,
    anchor
  );

  popover.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-insert]');
    if (!button) return;
    closePopover();
    if (button.dataset.insert === 'table') insertTable();
    else if (button.dataset.insert === 'link') await insertLinkDialog();
    else if (button.dataset.insert === 'file') await insertFileFromDisk();
    else if (button.dataset.insert === 'record') await startRecording();
  });
}

function insertBlockElement(element) {
  const block = blockAtCaret();
  if (block && block.parentNode) block.after(element);
  else dom.body.appendChild(element);

  const spacer = document.createElement('div');
  spacer.innerHTML = '<br>';
  element.after(spacer);
  placeCaretAtEnd(spacer);
  afterBodyEdit();
}

function insertTable() {
  if (!state.currentNote) return;
  const table = document.createElement('table');
  table.className = 'note-table';
  const tbody = document.createElement('tbody');
  for (let row = 0; row < 3; row++) {
    const tr = document.createElement('tr');
    for (let column = 0; column < 3; column++) {
      const cell = document.createElement('td');
      cell.innerHTML = '<br>';
      tr.appendChild(cell);
    }
    tbody.appendChild(tr);
  }
  table.appendChild(tbody);
  insertBlockElement(table);

  const first = table.querySelector('td');
  if (first) {
    const range = document.createRange();
    range.selectNodeContents(first);
    range.collapse(true);
    const selection = window.getSelection();
    selection.removeAllRanges();
    selection.addRange(range);
    dom.body.focus();
  }
  scheduleSave();
}

function normalizeUrl(value) {
  const url = String(value || '').trim();
  if (!url) return '';
  if (/^(https?:|mailto:)/i.test(url)) return url;
  return 'https://' + url;
}

async function insertLinkDialog() {
  if (!state.currentNote) return;
  const selection = saveSelection();
  const selectedText = selection ? selection.toString().trim() : '';
  const values = await promptFields({
    title: '插入链接',
    fields: [
      { name: 'url', label: '链接地址', required: true },
      { name: 'text', label: '显示文字（不填就用地址）' },
    ],
    okText: '插入',
  });
  if (!values) return;

  const url = normalizeUrl(values.url);
  const text = (values.text || selectedText || url).trim();
  const anchor = `<a href="${escapeHtml(url)}" target="_blank" rel="noreferrer">${escapeHtml(text)}</a>`;

  if (selection) {
    const sel = window.getSelection();
    sel.removeAllRanges();
    sel.addRange(selection);
    if (!selection.collapsed) {
      document.execCommand('createLink', false, url);
      afterBodyEdit();
      return;
    }
  }

  insertHtmlAtCaret(anchor);
}

function insertHtmlAtCaret(html) {
  dom.body.focus();
  const selection = window.getSelection();
  if (selection.rangeCount && dom.body.contains(selection.anchorNode)) {
    const range = selection.getRangeAt(0);
    range.deleteContents();
    const template = document.createElement('template');
    template.innerHTML = html;
    const fragment = template.content;
    const last = fragment.lastChild;
    range.insertNode(fragment);
    if (last) {
      const after = document.createRange();
      after.setStartAfter(last);
      after.collapse(true);
      selection.removeAllRanges();
      selection.addRange(after);
    }
  } else {
    dom.body.insertAdjacentHTML('beforeend', html);
    placeCaretAtEnd(dom.body);
  }
  afterBodyEdit();
}

async function insertFileFromDisk() {
  if (!state.currentNote) return;
  try {
    const picked = await call('pickFile');
    if (!picked) return;
    if (picked.kind === 'image' || String(picked.url || '').startsWith('https://media.local/')) {
      insertImage(picked);
    } else {
      insertAttachment(picked);
    }
  } catch (error) {
    toast('插入文件失败：' + error.message, 'error');
  }
}

function formatFileSize(size) {
  const value = Number(size) || 0;
  if (value < 1024) return value + ' B';
  if (value < 1024 * 1024) return (value / 1024).toFixed(1) + ' KB';
  return (value / 1024 / 1024).toFixed(1) + ' MB';
}

function insertAttachment(picked) {
  if (!state.currentNote) return;
  const url = picked.url;
  if (picked.kind === 'audio' || picked.kind === 'video') {
    const wrapper = document.createElement('div');
    wrapper.className = 'media-block';
    wrapper.contentEditable = 'false';
    wrapper.dataset.file = picked.file;
    const media = document.createElement(picked.kind);
    media.controls = true;
    media.preload = 'metadata';
    media.src = url;
    const remove = document.createElement('button');
    remove.className = 'media-del';
    remove.type = 'button';
    remove.title = '删除';
    remove.textContent = '×';
    wrapper.append(media, remove);
    insertBlockElement(wrapper);
  } else {
    const chip = document.createElement('a');
    chip.className = 'file-chip';
    chip.contentEditable = 'false';
    chip.dataset.file = picked.file;
    chip.href = url;
    chip.innerHTML = `<span class="file-chip-icon"><svg class="icon"><use href="#i-file"/></svg></span>
      <span class="file-chip-main">
        <span class="file-chip-name">${escapeHtml(picked.name || picked.file)}</span>
        <span class="file-chip-meta">${formatFileSize(picked.size)} · 点击打开</span>
      </span>`;
    insertBlockElement(chip);
  }
  toast('已插入附件');
}

function updateRecordTime() {
  const elapsed = Math.max(0, Math.floor((Date.now() - state.recordStartedAt) / 1000));
  dom.recordTime.textContent = `${pad(Math.floor(elapsed / 60))}:${pad(elapsed % 60)}`;
}

async function startRecording() {
  if (!state.currentNote || state.recording) return;
  try {
    await call('startRecording');
    state.recording = true;
    state.recordStartedAt = Date.now();
    updateRecordTime();
    dom.recordBar.classList.remove('hidden');
    clearInterval(state.recordTimer);
    state.recordTimer = setInterval(updateRecordTime, 1000);
  } catch (error) {
    toast('无法开始录音：' + error.message, 'error');
  }
}

async function stopRecording(silent = false) {
  if (!state.recording) return;
  state.recording = false;
  clearInterval(state.recordTimer);
  state.recordTimer = null;
  dom.recordBar.classList.add('hidden');

  try {
    const picked = await call('stopRecording');
    if (!silent) {
      insertAttachment(picked);
      toast('录音已插入');
    }
  } catch (error) {
    if (!silent) toast('录音失败：' + error.message, 'error');
  }
}

function openAiPopover(anchor) {
  const popover = mountPopover(
    `<div class="pop-title">本地智能工具 · 不联网</div>
     <button class="pop-item" type="button" data-ai="summary">
       <svg class="icon"><use href="#i-sparkles"/></svg><span>生成摘要</span>
     </button>
     <button class="pop-item" type="button" data-ai="title">
       <svg class="icon"><use href="#i-sparkles"/></svg><span>生成标题</span>
     </button>
     <button class="pop-item" type="button" data-ai="todos">
       <svg class="icon"><use href="#i-todo"/></svg><span>把列表转成待办</span>
     </button>
     <button class="pop-item" type="button" data-ai="mindmap">
       <svg class="icon"><use href="#i-ai"/></svg><span>生成脑图</span>
     </button>`,
    anchor
  );
  popover.addEventListener('click', async (event) => {
    const button = event.target.closest('[data-ai]');
    if (!button) return;
    closePopover();
    await runAiTool(button.dataset.ai);
  });
}

function makeLocalSummary(text, title) {
  const sentences = String(text || '')
    .replace(/\s+/g, ' ')
    .split(/(?<=[。！？.!?])\s*/)
    .map((sentence) => sentence.trim())
    .filter((sentence) => sentence.length >= 6);
  if (!sentences.length) return '';
  return sentences.slice(0, 3).join(' ');
}

async function runAiTool(tool) {
  if (!state.currentNote) return;
  const title = (state.currentNote.title || '').trim();
  const text = state.currentNote.text || extractPlainText();

  if (tool === 'summary') {
    const summary = makeLocalSummary(text, title);
    if (!summary) {
      toast('正文太短，暂时没有可摘要的内容');
      return;
    }
    dom.body.insertAdjacentHTML(
      'afterbegin',
      `<div class="ai-block"><b>摘要</b><br>${escapeHtml(summary)}</div><div><br></div>`
    );
    afterBodyEdit();
    toast('已在开头插入本地摘要');
    return;
  }

  if (tool === 'title') {
    if (title) {
      toast('已经有标题了');
      return;
    }
    const firstLine = String(text || '')
      .split(/\r?\n/)
      .map((line) => line.trim())
      .find(Boolean);
    const generated = (firstLine || '无标题备忘录').replace(/^[#>*\-\s]+/, '').slice(0, 24);
    state.currentNote.title = generated;
    dom.titleInput.value = generated;
    scheduleSave();
    updateCardInList(state.currentNote);
    toast('已生成标题');
    return;
  }

  if (tool === 'todos') {
    let count = 0;
    const blocks = Array.from(dom.body.children);
    for (const block of blocks) {
      if (block.classList.contains('todo-item')) continue;
      const value = block.textContent.trim();
      const match = value.match(/^(?:[-*•]|\[\s?\]|\[[xX]\]|\d+[.)])\s*(.+)$/);
      if (!match) continue;
      const checked = /^\[[xX]\]/.test(value);
      const item = createTodoItem(match[1].trim(), checked);
      block.replaceWith(item);
      count++;
    }
    if (!count) {
      toast('没有找到可以转换的列表行（试试以 - / 1. 开头）');
      return;
    }
    afterBodyEdit();
    toast(`已转换 ${count} 条待办`);
    return;
  }

  if (tool === 'mindmap') {
    const headings = Array.from(dom.body.children)
      .filter((element) => element.matches('h1,h2,h3,.md-h1,.md-h2,.md-h3'))
      .map((element) => element.textContent.trim())
      .filter(Boolean)
      .slice(0, 6);

    const nodes = headings.length
      ? headings
      : Array.from(dom.body.children)
          .map((element) => element.textContent.trim())
          .filter((value) => value.length > 0)
          .slice(0, 6);

    if (!nodes.length) {
      toast('正文太短，暂时生成不了脑图');
      return;
    }

    const root = title || nodes[0] || '笔记';
    const mindmap = document.createElement('div');
    mindmap.className = 'mind-map';
    mindmap.contentEditable = 'false';
    mindmap.innerHTML = `<div class="mind-root">${escapeHtml(root)}</div>
      <div class="mind-branches">
        ${nodes
          .slice(0, 6)
          .map(
            (node, index) =>
              `<div class="mind-branch"><span class="mind-index">${index + 1}</span><span>${escapeHtml(node)}</span></div>`
          )
          .join('')}
      </div>`;
    insertBlockElement(mindmap);
    toast('已生成结构化脑图（本地规则）');
  }
}

function closeModalQuietly() {
  dom.modalLayer.classList.add('hidden');
  dom.modalCard.innerHTML = '';
  state.modalResolve = null;
}

/* ================= 下拉操作 ================= */

async function exportNote() {
  if (!state.currentNote) return;
  await flushSave();
  try {
    const result = await call('exportText', {
      title: state.currentNote.title.trim() || '无标题备忘录',
      text: state.currentNote.text,
    });
    if (result && result.path) toast('已导出到 ' + result.path);
  } catch (error) {
    toast('导出失败：' + error.message, 'error');
  }
}

async function copyFullText() {
  if (!state.currentNote) return;
  const text = state.currentNote.text || '';
  try {
    await navigator.clipboard.writeText(text);
    toast('已复制全文');
  } catch {
    const textarea = document.createElement('textarea');
    textarea.value = text;
    textarea.style.position = 'fixed';
    textarea.style.opacity = '0';
    document.body.appendChild(textarea);
    textarea.select();
    document.execCommand('copy');
    textarea.remove();
    toast('已复制全文');
  }
}

/* ================= 待办清单 ================= */

function createTodoItem(text, checked) {
  const item = document.createElement('div');
  item.className = 'todo-item';
  item.dataset.checked = checked ? 'true' : 'false';
  item.innerHTML =
    '<span class="todo-box" contenteditable="false"></span><span class="todo-text"></span>';
  item.querySelector('.todo-text').textContent = text || '';
  return item;
}

function insertTodo() {
  if (!state.currentNote) return;
  const block = blockAtCaret();

  if (block && block.classList.contains('todo-item')) {
    const text = block.querySelector('.todo-text');
    const div = document.createElement('div');
    div.innerHTML = text ? text.innerHTML || '<br>' : '<br>';
    block.replaceWith(div);
    focusEnd(div);
  } else if (block) {
    const item = createTodoItem(block.textContent.trim(), false);
    block.replaceWith(item);
    focusEnd(item.querySelector('.todo-text'));
  } else {
    const item = createTodoItem('', false);
    dom.body.appendChild(item);
    focusEnd(item.querySelector('.todo-text'));
  }

  afterBodyEdit();
}

function onBodyKeyDown(event) {
  if (event.isComposing || event.keyCode === 229) return;

  if (event.key === 'Enter' && !event.shiftKey && !event.ctrlKey && !event.altKey) {
    const textEl = currentTodoText();
    if (!textEl) return;

    event.preventDefault();
    const item = textEl.closest('.todo-item');
    if (!textEl.textContent.trim()) {
      const div = document.createElement('div');
      div.innerHTML = '<br>';
      item.replaceWith(div);
      placeCaretAtEnd(div);
    } else {
      const newItem = createTodoItem('', false);
      item.after(newItem);
      focusEnd(newItem.querySelector('.todo-text'));
    }
    afterBodyEdit();
    return;
  }

  if (event.key === 'Backspace') {
    const selection = window.getSelection();
    if (!selection.rangeCount || !selection.isCollapsed) return;
    const textEl = currentTodoText();
    if (!textEl) return;
    if (textEl.textContent.length > 0) return;

    event.preventDefault();
    const item = textEl.closest('.todo-item');
    const previous = item.previousElementSibling;
    item.remove();
    if (previous) {
      const previousText = previous.classList.contains('todo-item')
        ? previous.querySelector('.todo-text')
        : previous;
      focusEnd(previousText || previous);
    } else {
      dom.body.focus();
      placeCaretAtEnd(dom.body);
    }
    afterBodyEdit();
  }
}

function currentTodoText() {
  const selection = window.getSelection();
  if (!selection.rangeCount) return null;
  let node = selection.anchorNode;
  if (!node) return null;
  if (node.nodeType === Node.TEXT_NODE) node = node.parentElement;
  return node.closest ? node.closest('.todo-text') : null;
}

function toggleTodo(item) {
  if (!item) return;
  item.dataset.checked = item.dataset.checked === 'true' ? 'false' : 'true';
  afterBodyEdit();
}

/* ================= 正文事件 ================= */

function onBodyClick(event) {
  const mediaRemove = event.target.closest('.media-del');
  if (mediaRemove) {
    event.preventDefault();
    const block = mediaRemove.closest('.media-block');
    if (block) {
      block.remove();
      afterBodyEdit();
    }
    return;
  }

  const fileChip = event.target.closest('.file-chip');
  if (fileChip) {
    event.preventDefault();
    if (fileChip.dataset.file) {
      call('openFile', { file: fileChip.dataset.file }).catch((error) => toast(error.message, 'error'));
    }
    return;
  }
  const box = event.target.closest('.todo-box');
  if (box) {
    event.preventDefault();
    toggleTodo(box.closest('.todo-item'));
    return;
  }

  const remove = event.target.closest('.img-del');
  if (remove) {
    event.preventDefault();
    const figure = remove.closest('figure.img');
    if (figure) {
      if (state.selectedImage === figure.dataset.file) {
        state.selectedImage = null;
      }
      figure.remove();
      afterBodyEdit();
    }
    return;
  }

  const figure = event.target.closest('figure.img');
  if (figure) {
    selectImage(figure);
    return;
  }

  deselectImage();
}

function onBodyPaste(event) {
  const items = event.clipboardData ? Array.from(event.clipboardData.items) : [];
  const imageItem = items.find((item) => item.type.startsWith('image/'));

  if (imageItem) {
    event.preventDefault();
    const file = imageItem.getAsFile();
    if (!file) return;

    readFileAsDataUrl(file)
      .then((dataUrl) => call('saveImage', { dataUrl }))
      .then((picked) => {
        insertImage(picked);
        toast('已粘贴图片');
      })
      .catch((error) => toast('粘贴图片失败：' + error.message, 'error'));
    return;
  }

  event.preventDefault();
  const text = event.clipboardData.getData('text/plain');
  if (text) {
    document.execCommand('insertText', false, text);
    afterBodyEdit();
  }
}

function onBodyDragOver(event) {
  if (Array.from(event.dataTransfer?.types || []).includes('Files')) {
    event.preventDefault();
    event.dataTransfer.dropEffect = 'copy';
  }
}

async function onBodyDrop(event) {
  const files = Array.from(event.dataTransfer?.files || []).filter((file) =>
    file.type.startsWith('image/')
  );
  if (!files.length) return;

  event.preventDefault();
  for (const file of files) {
    try {
      const dataUrl = await readFileAsDataUrl(file);
      const picked = await call('saveImage', { dataUrl });
      insertImage(picked);
    } catch (error) {
      toast('插入图片失败：' + error.message, 'error');
    }
  }
}

/* ================= 图片 ================= */

async function insertImageFromDisk() {
  if (!state.currentNote) return;
  const selection = saveSelection();
  if (selection) state.lastRange = selection;
  try {
    const picked = await call('pickImage');
    if (!picked) return;
    insertImage(picked);
  } catch (error) {
    toast('插入图片失败：' + error.message, 'error');
  }
}

function insertImage(picked) {
  if (!picked || !state.currentNote) return;

  const figure = document.createElement('figure');
  figure.className = 'img';
  figure.contentEditable = 'false';
  figure.dataset.file = picked.file;

  const image = document.createElement('img');
  image.src = picked.url;
  image.alt = '';
  image.dataset.file = picked.file;

  const remove = document.createElement('button');
  remove.className = 'img-del';
  remove.type = 'button';
  remove.title = '删除图片';
  remove.setAttribute('aria-label', '删除图片');

  figure.append(image, remove);

  const spacer = document.createElement('div');
  spacer.innerHTML = '<br>';

  const block = blockAtCaret();
  if (block && block.parentNode) {
    block.after(figure, spacer);
  } else {
    dom.body.append(figure, spacer);
  }

  placeCaretAtEnd(spacer);
  updateEmptyBody();
  scheduleSave();
  selectImage(figure);
}

function selectImage(figure) {
  deselectImage();
  figure.classList.add('selected');
  state.selectedImage = figure.dataset.file;
}

function deselectImage() {
  state.selectedImage = null;
  dom.body.querySelectorAll('figure.img.selected').forEach((figure) => {
    figure.classList.remove('selected');
  });
}

function findFigure(file) {
  if (!file) return null;
  return Array.from(dom.body.querySelectorAll('figure[data-file]')).find(
    (figure) => figure.dataset.file === file
  ) || null;
}

function readFileAsDataUrl(file) {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result);
    reader.onerror = () => reject(new Error('读取图片失败'));
    reader.readAsDataURL(file);
  });
}

/* ================= 文字识别 ================= */

async function runOcr() {
  if (!state.currentNote) return;

  let figure = findFigure(state.selectedImage);
  if (!figure) {
    try {
      const picked = await call('pickImage');
      if (!picked) return;
      insertImage(picked);
      figure = findFigure(picked.file);
    } catch (error) {
      toast('选择图片失败：' + error.message, 'error');
      return;
    }
  }

  if (!figure) return;
  const file = figure.dataset.file;
  const button = dom.toolbar.querySelector('[data-act="ocr"]');
  button.classList.add('busy');
  toast('正在识别图片文字…');

  try {
    const result = await call('ocr', { file });
    const text = (result?.text || '').trim();
    if (!text) {
      toast('没有识别到文字');
      return;
    }

    const lines = text.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
    let anchor = figure;
    for (const line of lines) {
      const div = document.createElement('div');
      div.textContent = line;
      if (anchor && anchor.parentNode) {
        anchor.after(div);
      } else {
        dom.body.appendChild(div);
      }
      anchor = div;
    }

    deselectImage();
    afterBodyEdit();
    toast(`识别完成，已插入 ${lines.length} 行文字`);
  } catch (error) {
    toast('识别失败：' + error.message, 'error');
  } finally {
    button.classList.remove('busy');
  }
}

/* ================= 语音 ================= */

async function toggleVoice() {
  if (state.voiceOn) {
    await stopVoice();
    return;
  }
  if (!state.currentNote) return;

  const selection = saveSelection();
  if (selection) {
    state.voiceRange = selection;
  } else {
    dom.body.focus();
    placeCaretAtEnd(dom.body);
    state.voiceRange = saveSelection();
  }

  state.voiceNoteId = state.currentNote.id;
  state.voiceOn = true;
  dom.voiceHypo.textContent = '正在启动语音识别…';
  dom.voiceTitle.textContent = '正在聆听…';
  dom.voiceBar.classList.remove('hidden');
  dom.btnVoice.classList.add('active');

  try {
    const result = await call('voiceStart');
    if (result?.listening) {
      state.voiceOn = true;
    }
  } catch (error) {
    setVoiceUi(false);
    toast(error.message, 'error');
  }
}

async function stopVoice() {
  try {
    await call('voiceStop');
  } catch {
    // 忽略
  }
  setVoiceUi(false);
}

function setVoiceUi(on) {
  state.voiceOn = on;
  dom.voiceBar.classList.toggle('hidden', !on);
  dom.btnVoice.classList.toggle('active', on);
  if (!on) {
    dom.voiceHypo.textContent = '请开始说话，说完点「完成」';
    dom.voiceTitle.textContent = '正在聆听…';
  }
}

function insertVoiceText(text) {
  if (!state.currentNote || !text) return;
  if (state.voiceNoteId && state.currentNote.id !== state.voiceNoteId) return;

  dom.body.focus();
  const selection = window.getSelection();
  let range = state.voiceRange;

  const live = selection.rangeCount ? selection.getRangeAt(0) : null;
  if (live && dom.body.contains(live.startContainer)) {
    range = live;
  }

  if (range && dom.body.contains(range.startContainer)) {
    range.deleteContents();
    const textNode = document.createTextNode(text);
    range.insertNode(textNode);
    range.setStartAfter(textNode);
    range.collapse(true);
    selection.removeAllRanges();
    selection.addRange(range);
    state.voiceRange = range.cloneRange();
  } else {
    dom.body.appendChild(document.createTextNode(text));
    placeCaretAtEnd(dom.body);
  }

  afterBodyEdit();
}

function handleNativeEvent(name, data) {
  if (name === 'quickNote') {
    newNote();
    return;
  }

  if (name === 'voiceState') {
    if (data.state === 'listening') {
      setVoiceUi(true);
      dom.voiceTitle.textContent = '正在聆听…';
      dom.voiceHypo.textContent = '请开始说话，说完点「完成」';
    } else if (data.state === 'stopped') {
      setVoiceUi(false);
    } else if (data.state === 'error') {
      setVoiceUi(false);
      toast(data.message || '语音识别出错', 'error');
    }
    return;
  }

  if (name === 'voiceText') {
    if (data.text) insertVoiceText(data.text);
    return;
  }

  if (name === 'voiceHypo') {
    if (state.voiceOn && data.text) {
      dom.voiceHypo.textContent = data.text;
    }
  }
}

/* ================= 手写 ================= */

function openDraw() {
  if (!state.currentNote) return;
  dom.drawLayer.classList.remove('hidden');
  requestAnimationFrame(initCanvas);
}

function closeDraw() {
  dom.drawLayer.classList.add('hidden');
  state.draw.drawing = false;
}

function initCanvas() {
  const canvas = dom.drawCanvas;
  const rect = canvas.getBoundingClientRect();
  const dpr = window.devicePixelRatio || 1;
  state.draw.dpr = dpr;
  canvas.width = Math.max(1, Math.floor(rect.width * dpr));
  canvas.height = Math.max(1, Math.floor(rect.height * dpr));

  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.fillStyle = '#FFFFFF';
  ctx.fillRect(0, 0, rect.width, rect.height);
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  state.draw.last = null;
}

function canvasPoint(event) {
  const rect = dom.drawCanvas.getBoundingClientRect();
  return {
    x: event.clientX - rect.left,
    y: event.clientY - rect.top,
  };
}

function drawDot(point, pressure) {
  const ctx = dom.drawCanvas.getContext('2d');
  const tool = state.draw.tool;
  const color = state.draw.color;
  const width = Math.max(1, state.draw.width);
  ctx.save();
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';

  if (tool === 'eraser') {
    ctx.fillStyle = '#FFFFFF';
    ctx.beginPath();
    ctx.arc(point.x, point.y, width * 1.1, 0, Math.PI * 2);
    ctx.fill();
  } else if (tool === 'brush') {
    ctx.fillStyle = color;
    ctx.globalAlpha = 0.9;
    ctx.beginPath();
    ctx.arc(point.x, point.y, width * (0.9 + pressure * 0.5), 0, Math.PI * 2);
    ctx.fill();
  } else if (tool === 'pencil') {
    ctx.fillStyle = color;
    ctx.globalAlpha = 0.55;
    for (let i = 0; i < 3; i++) {
      ctx.beginPath();
      ctx.arc(point.x + (Math.random() - 0.5) * 2.2, point.y + (Math.random() - 0.5) * 2.2, Math.max(0.6, width * 0.35), 0, Math.PI * 2);
      ctx.fill();
    }
  } else {
    ctx.fillStyle = color;
    ctx.beginPath();
    ctx.arc(point.x, point.y, width / 2, 0, Math.PI * 2);
    ctx.fill();
  }
  ctx.restore();
}

function drawSegment(from, to, pressure) {
  const ctx = dom.drawCanvas.getContext('2d');
  const tool = state.draw.tool;
  const color = state.draw.color;
  const width = Math.max(1, state.draw.width);
  const distance = Math.hypot(to.x - from.x, to.y - from.y);
  ctx.save();
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';

  if (tool === 'eraser') {
    ctx.strokeStyle = '#FFFFFF';
    ctx.lineWidth = width * 2.2;
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    ctx.stroke();
  } else if (tool === 'brush') {
    const speedBoost = 1 + Math.min(1.1, distance / 28);
    const brushWidth = width * speedBoost * (0.75 + pressure * 0.55);
    ctx.strokeStyle = color;
    ctx.globalAlpha = 0.9;
    ctx.lineWidth = Math.max(1, brushWidth);
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    ctx.stroke();
    ctx.globalAlpha = 0.22;
    ctx.lineWidth = Math.max(1, brushWidth * 1.8);
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    ctx.stroke();
  } else if (tool === 'pencil') {
    ctx.strokeStyle = color;
    ctx.globalAlpha = 0.5;
    ctx.lineWidth = Math.max(0.7, width * 0.7);
    for (let i = 0; i < 3; i++) {
      const jitter = (Math.random() - 0.5) * 1.6;
      ctx.beginPath();
      ctx.moveTo(from.x + jitter, from.y + jitter);
      ctx.lineTo(to.x + jitter, to.y + jitter);
      ctx.stroke();
    }
  } else {
    ctx.strokeStyle = color;
    ctx.lineWidth = width;
    ctx.beginPath();
    ctx.moveTo(from.x, from.y);
    ctx.lineTo(to.x, to.y);
    ctx.stroke();
  }
  ctx.restore();
}

function bindCanvas() {
  const canvas = dom.drawCanvas;

  canvas.addEventListener('pointerdown', (event) => {
    if (event.button !== 0) return;
    canvas.setPointerCapture(event.pointerId);
    state.draw.drawing = true;
    const point = canvasPoint(event);
    state.draw.last = point;
    drawDot(point, event.pressure || 0.5);
  });

  canvas.addEventListener('pointermove', (event) => {
    if (!state.draw.drawing) return;
    const point = canvasPoint(event);
    if (state.draw.last) {
      drawSegment(state.draw.last, point, event.pressure || 0.5);
    }
    state.draw.last = point;
  });

  const stop = () => {
    state.draw.drawing = false;
    state.draw.last = null;
  };
  canvas.addEventListener('pointerup', stop);
  canvas.addEventListener('pointercancel', stop);
}

async function insertDrawing() {
  const dataUrl = dom.drawCanvas.toDataURL('image/png');
  closeDraw();
  try {
    const picked = await call('saveImage', { dataUrl });
    insertImage(picked);
    toast('手写已插入');
  } catch (error) {
    toast('插入手写失败：' + error.message, 'error');
  }
}

/* ================= 选区工具 ================= */

function saveSelection() {
  const selection = window.getSelection();
  if (selection.rangeCount && dom.body.contains(selection.anchorNode)) {
    return selection.getRangeAt(0).cloneRange();
  }
  return null;
}

function blockAtCaret() {
  let range = null;
  const selection = window.getSelection();
  if (selection.rangeCount && dom.body.contains(selection.anchorNode)) {
    range = selection.getRangeAt(0);
  } else if (state.lastRange && dom.body.contains(state.lastRange.startContainer)) {
    range = state.lastRange;
  }
  if (!range) return null;

  let node = range.startContainer;
  if (node.nodeType === Node.TEXT_NODE) node = node.parentElement;
  if (!node || !dom.body.contains(node)) return null;

  const block = node.closest('div,p,li');
  return block && block !== dom.body ? block : null;
}

function placeCaretAtEnd(element) {
  const range = document.createRange();
  range.selectNodeContents(element);
  range.collapse(false);
  const selection = window.getSelection();
  selection.removeAllRanges();
  selection.addRange(range);
  element.focus();
}

function focusEnd(element) {
  if (!element) return;
  if (element.classList && element.classList.contains('body')) {
    placeCaretAtEnd(element);
    return;
  }
  const range = document.createRange();
  range.selectNodeContents(element);
  range.collapse(false);
  const selection = window.getSelection();
  selection.removeAllRanges();
  selection.addRange(range);
  const editable = element.closest('[contenteditable="true"]');
  if (editable) editable.focus();
}

function afterBodyEdit() {
  updateEmptyBody();
  scheduleSave();
}

/* ================= 外观 / 主题 ================= */

const THEME_KIT = window.WinNoteThemeKit || {
  builtinThemes: [],
  accents: [{ id: 'blue', name: '蓝', light: '#0A59F7', dark: '#4C8DFF' }],
  radiusPresets: [{ id: 'squircle', name: '苹果连续圆角', description: '', radius: '17px', sm: '10px', lg: '24px', xl: '28px', corner: 'superellipse(4)' }],
  getAccent: () => ({ light: '#0A59F7', dark: '#4C8DFF' }),
  getRadius: () => ({ id: 'squircle' }),
  getCustomThemes: () => [],
};

function normalizeAppearance(raw) {
  const source = raw || {};
  const mode = (source.mode || state.settings.theme || 'light') === 'dark' ? 'dark' : 'light';
  return {
    mode,
    themeId: source.themeId || 'flat',
    accent: source.accent || 'blue',
    radiusStyle: source.radiusStyle || 'squircle',
    animations: source.animations !== false,
    topSafeInset: Math.max(0, Math.min(80, Number(source.topSafeInset) || 0)),
  };
}

function hexToRgba(hex, alpha) {
  const value = String(hex || '').replace('#', '');
  if (!/^[0-9a-f]{6}$/i.test(value)) {
    return `rgba(10, 89, 247, ${alpha})`;
  }
  const r = parseInt(value.slice(0, 2), 16);
  const g = parseInt(value.slice(2, 4), 16);
  const b = parseInt(value.slice(4, 6), 16);
  return `rgba(${r}, ${g}, ${b}, ${alpha})`;
}

function themeById(id) {
  const builtins = THEME_KIT.builtinThemes || [];
  const custom = state.customThemes || [];
  return builtins.find((theme) => theme.id === id) || custom.find((theme) => theme.id === id) || builtins[0];
}

function allThemes() {
  return [...(THEME_KIT.builtinThemes || []), ...(state.customThemes || [])];
}

async function loadCustomThemes(files) {
  state.customThemeFiles = Array.isArray(files) ? files.slice() : [];
  state.customThemes = [];
  window.__WINNOTE_CUSTOM_THEMES__ = {};
  document.querySelectorAll('script[data-custom-theme]').forEach((script) => script.remove());

  for (const file of state.customThemeFiles) {
    try {
      const before = new Set(Object.keys(window.__WINNOTE_CUSTOM_THEMES__ || {}));
      await new Promise((resolve) => {
        const script = document.createElement('script');
        script.dataset.customTheme = file;
        script.src = `https://appearance.local/${encodeURIComponent(file)}?v=${Date.now()}`;
        script.onload = resolve;
        script.onerror = resolve;
        document.head.appendChild(script);
      });
      if (window.__WINNOTE_THEME__) {
        window.WinNote.registerTheme(window.__WINNOTE_THEME__);
        delete window.__WINNOTE_THEME__;
      }
      for (const [id, theme] of Object.entries(window.__WINNOTE_CUSTOM_THEMES__ || {})) {
        if (!before.has(id) && theme && !theme.file) {
          theme.file = file;
        }
      }
    } catch {
      // 单个主题失败不影响其它主题
    }
  }

  const builtinIds = new Set((THEME_KIT.builtinThemes || []).map((theme) => theme.id));
  state.customThemes = (THEME_KIT.getCustomThemes() || []).map((theme) => {
    const copy = { ...theme, custom: true };
    if (builtinIds.has(copy.id)) copy.id = 'custom-' + copy.id;
    return copy;
  });

  if (!themeById(state.appearance.themeId)) {
    state.appearance.themeId = 'flat';
  }
}

function applyAppearance({ persist = true, syncChrome = true } = {}) {
  state.appearance = normalizeAppearance(state.appearance);
  state.settings.theme = state.appearance.mode;
  const theme = themeById(state.appearance.themeId) || THEME_KIT.builtinThemes[0];
  if (!theme) return;

  const mode = state.appearance.mode;
  const palette = Object.assign({}, theme[mode] || theme.light || {});
  const accent = THEME_KIT.getAccent(state.appearance.accent);
  const accentColor = accent?.[mode] || accent?.light || '#0A59F7';
  const accentSoft = hexToRgba(accentColor, mode === 'dark' ? 0.18 : 0.1);
  const accentShadow = hexToRgba(accentColor, mode === 'dark' ? 0.28 : 0.22);

  const vars = {
    '--bg': palette.bg,
    '--panel': palette.panel,
    '--panel-2': palette.panel2,
    '--text': palette.text,
    '--text-2': palette.text2,
    '--text-3': palette.text3,
    '--line': palette.line,
    '--line-strong': palette.lineStrong,
    '--hover': palette.hover,
    '--active': palette.active,
    '--danger': palette.danger,
    '--danger-soft': palette.dangerSoft,
    '--search-bg': palette.searchBg,
    '--card-line': palette.cardLine,
    '--card-shadow': palette.cardShadow,
    '--card-shadow-hover': palette.cardShadowHover,
    '--shadow-lg': palette.shadowLg,
    '--toolbar-shadow': palette.toolbarShadow,
    '--scroll-thumb': palette.scrollThumb,
    '--app-bg': palette.appBg || palette.bg,
    '--glass-blur': palette.glassBlur || '0px',
    '--accent': accentColor,
    '--accent-soft': accentSoft,
    '--accent-shadow': accentShadow,
  };

  for (const [key, value] of Object.entries(vars)) {
    if (value) document.body.style.setProperty(key, value);
  }

  document.body.dataset.theme = mode;
  document.body.dataset.style = theme.id;
  document.body.dataset.radius = state.appearance.radiusStyle;
  document.body.dataset.anim = state.appearance.animations ? 'on' : 'off';

  let customStyle = document.getElementById('customThemeCss');
  if (!customStyle) {
    customStyle = document.createElement('style');
    customStyle.id = 'customThemeCss';
    document.head.appendChild(customStyle);
  }
  customStyle.textContent = theme.css || '';

  updateThemeButton();
  if (state.currentNote) applyEditorColor(state.currentNote);

  if (syncChrome) {
    const solid = (value, fallback) => (/^#[0-9a-f]{6}$/i.test(value || '') ? value : fallback);
    call('setChrome', {
      bg: solid(palette.panelSolid, mode === 'dark' ? '#17181C' : '#FFFFFF'),
      fg: solid(palette.text, mode === 'dark' ? '#ECEDEF' : '#1A1A1C'),
      sub: solid(palette.text3, mode === 'dark' ? '#8D9097' : '#7B7D85'),
      hover: solid(palette.panel2, mode === 'dark' ? '#25272C' : '#E4E6EA'),
      pressed: solid(palette.lineStrong, mode === 'dark' ? '#2F3137' : '#D9DBE0'),
      line: solid(palette.line, mode === 'dark' ? '#26282E' : '#E5E7EC'),
    }).catch(() => {});
  }

  if (persist) {
    clearTimeout(appearanceSaveTimer);
    appearanceSaveTimer = setTimeout(() => {
      call('saveAppearance', {
        themeId: state.appearance.themeId,
        accent: state.appearance.accent,
        radiusStyle: state.appearance.radiusStyle,
        animations: state.appearance.animations,
        topSafeInset: state.appearance.topSafeInset,
      }).catch((error) => toast('保存外观失败：' + error.message, 'error'));
    }, 120);
  }
}

function applyTheme(theme, persist = true) {
  const mode = theme === 'dark' ? 'dark' : 'light';
  state.settings.theme = mode;
  state.appearance.mode = mode;
  applyAppearance({ persist: false, syncChrome: true });
  renderList();
  if (persist) {
    call('setTheme', { theme: mode }).catch(() => {});
  }
  if (state.settingsOpen) renderAppearanceControls();
}

function updateThemeButton() {
  const dark = state.settings.theme === 'dark';
  dom.themeLabel.textContent = dark ? '浅色模式' : '深色模式';
  dom.themeIcon.setAttribute('href', dark ? '#i-sun' : '#i-moon');
}

/* ================= 设置大页面 ================= */

function openSettingsPage(section = 'appearance') {
  state.settingsOpen = true;
  renderSettingsPage();
  dom.settingsPage.classList.remove('hidden', 'closing');
  document.body.classList.add('settings-open');
  requestAnimationFrame(() => {
    dom.settingsScroll.scrollTop = 0;
    scrollToSettingsSection(section, false);
  });
}

function closeSettingsPage() {
  if (!state.settingsOpen) return;
  state.settingsOpen = false;
  dom.settingsPage.classList.add('closing');
  document.body.classList.remove('settings-open');
  setTimeout(() => {
    if (state.settingsOpen) return;
    dom.settingsPage.classList.add('hidden');
    dom.settingsPage.classList.remove('closing');
  }, 170);
  renderSidebar();
  renderList();
}

function scrollToSettingsSection(section, smooth = true) {
  const target = document.querySelector(`.settings-section[data-settings-section="${CSS.escape(section)}"]`);
  if (!target) return;
  dom.settingsNav.querySelectorAll('[data-settings-section]').forEach((button) => {
    button.classList.toggle('active', button.dataset.settingsSection === section);
  });
  target.scrollIntoView({ behavior: smooth ? 'smooth' : 'auto', block: 'start' });
}

function filterSettingsSections() {
  const query = dom.settingsSearch.value.trim().toLowerCase();
  dom.settingsPage.querySelectorAll('.settings-section').forEach((section) => {
    const match = !query || section.innerText.toLowerCase().includes(query);
    section.classList.toggle('hidden', !match);
  });
  dom.settingsNav.querySelectorAll('[data-settings-section]').forEach((button) => {
    const section = document.querySelector(`.settings-section[data-settings-section="${CSS.escape(button.dataset.settingsSection)}"]`);
    button.classList.toggle('hidden', !!section && section.classList.contains('hidden'));
  });
}

function renderSettingsPage() {
  renderAppearanceControls();
  renderCustomThemes();
  renderSettingsProfiles();
  dom.appearancePath.textContent = state.settings.appearanceDir ? '当前目录：' + state.settings.appearanceDir : '';
  dom.settingsDataPath.textContent = state.profile.path ? '当前路径：' + state.profile.path : '';
  dom.privacyPasscodeText.textContent = state.settings.hasPasscode
    ? `已设置密码 · ${state.notes.filter((note) => note.locked && !note.deleted).length} 条笔记已锁定`
    : '未设置，可锁定单条笔记。';
  dom.btnPrivacyPasscode.textContent = state.settings.hasPasscode ? '更改密码' : '设置密码';
  dom.topInset.value = String(state.appearance.topSafeInset || 0);
  dom.insetValue.textContent = (state.appearance.topSafeInset || 0) + ' px';
}

function renderAppearanceControls() {
  const activeTheme = themeById(state.appearance.themeId) || allThemes()[0];
  dom.activeThemeName.textContent = activeTheme ? activeTheme.name : '平面';

  dom.themeGrid.innerHTML = allThemes().map((theme) => {
    const palette = theme[state.appearance.mode] || theme.light || {};
    const accent = THEME_KIT.getAccent(state.appearance.accent);
    const accentColor = accent?.[state.appearance.mode] || accent?.light || '#0A59F7';
    const active = theme.id === state.appearance.themeId;
    return `<button class="theme-card ${active ? 'active' : ''}" type="button" data-theme-id="${escapeHtml(theme.id)}">
      <div class="theme-preview" style="background:${escapeHtml(palette.appBg || palette.bg || '#eee')}">
        <i class="tp-side" style="background:${escapeHtml(palette.panel || '#fff')}"></i>
        <i class="tp-list" style="background:${escapeHtml(palette.panel2 || palette.bg || '#f5f5f5')}"></i>
        <i class="tp-card" style="background:${escapeHtml(palette.panel || '#fff')}"></i>
        <i class="tp-accent" style="background:${escapeHtml(accentColor)}"></i>
      </div>
      <strong>${escapeHtml(theme.name || theme.id)}${theme.custom ? ' · 自定义' : ''}</strong>
      <span>${escapeHtml(theme.subtitle || theme.description || '')}</span>
      ${active ? '<i class="theme-check"><svg class="icon"><use href="#i-check"/></svg></i>' : ''}
    </button>`;
  }).join('');

  dom.modeOptions.querySelectorAll('[data-mode]').forEach((button) => {
    button.classList.toggle('active', button.dataset.mode === state.appearance.mode);
  });

  dom.accentGrid.innerHTML = (THEME_KIT.accents || []).map((accent) => {
    const color = accent[state.appearance.mode] || accent.light;
    const active = accent.id === state.appearance.accent;
    return `<button class="accent-swatch ${active ? 'active' : ''}" type="button" data-accent-id="${escapeHtml(accent.id)}"
      style="background:${escapeHtml(color)}" title="${escapeHtml(accent.name)}">
      ${active ? '<svg class="icon"><use href="#i-check"/></svg>' : ''}
    </button>`;
  }).join('');

  dom.radiusGrid.innerHTML = (THEME_KIT.radiusPresets || []).map((radius) => {
    const active = radius.id === state.appearance.radiusStyle;
    return `<button class="radius-card ${active ? 'active' : ''}" type="button" data-radius-id="${escapeHtml(radius.id)}">
      <div class="radius-demo"></div>
      <strong>${escapeHtml(radius.name)}</strong>
      <span>${escapeHtml(radius.description || '')}</span>
    </button>`;
  }).join('');

  dom.animToggle.setAttribute('aria-checked', state.appearance.animations ? 'true' : 'false');
  dom.animToggle.setAttribute('title', state.appearance.animations ? '关闭动画' : '开启动画');
}

function renderCustomThemes() {
  const list = state.customThemes || [];
  dom.customThemeList.innerHTML = list.length
    ? list.map((theme) => {
        const palette = theme.light || {};
        const active = theme.id === state.appearance.themeId;
        return `<div class="custom-theme-item">
          <i class="custom-theme-swatch" style="background:linear-gradient(135deg, ${escapeHtml(palette.bg || '#eee')}, ${escapeHtml(palette.panel || '#fff')})"></i>
          <div class="custom-theme-main">
            <strong>${escapeHtml(theme.name || theme.id)}${active ? ' · 使用中' : ''}</strong>
            <span>${escapeHtml(theme.description || theme.file || '')}</span>
          </div>
          <div class="custom-theme-actions">
            <button class="mini-btn" type="button" data-use-theme="${escapeHtml(theme.id)}">使用</button>
            <button class="mini-btn danger" type="button" data-delete-theme="${escapeHtml(theme.file || '')}">删除</button>
          </div>
        </div>`;
      }).join('')
    : '<div class="sidebar-empty">还没有自定义主题。导入一个 .js 文件即可。</div>';
}

function renderSettingsProfiles() {
  const profiles = state.profile?.profiles || [];
  dom.settingsProfileList.innerHTML = profiles.map((profile) => {
    const active = profile.id === state.profile?.id;
    return `<div class="profile-item ${active ? 'active' : ''}">
      <div class="profile-main">
        <div class="profile-name">${escapeHtml(profile.name)}${active ? ' · 当前' : ''}</div>
        <div class="profile-path" title="${escapeHtml(profile.path)}">${escapeHtml(profile.path)}</div>
      </div>
      ${active ? '' : `<button class="mini-btn" type="button" data-switch-profile="${escapeHtml(profile.id)}">切换</button>`}
    </div>`;
  }).join('');
}

async function importCustomTheme() {
  try {
    const before = new Set((state.customThemes || []).map((theme) => theme.id));
    const imported = await call('importTheme');
    if (!imported) return;
    const result = await call('listCustomThemes');
    await loadCustomThemes(result.files || []);
    const added = (state.customThemes || []).find((theme) => !before.has(theme.id));
    if (added) {
      state.appearance.themeId = added.id;
      applyAppearance();
      renderSidebar();
    }
    renderSettingsPage();
    toast(added ? '已导入并启用「' + (added.name || added.id) + '」' : '主题已导入，请在列表中选择');
  } catch (error) {
    toast('导入主题失败：' + error.message, 'error');
  }
}

async function importThemeLibrary() {
  try {
    const before = new Set((state.customThemes || []).map((theme) => theme.id));
    const result = await call('importThemeLibrary');
    await loadCustomThemes(result.files || []);
    const added = (state.customThemes || []).filter((theme) => !before.has(theme.id));
    renderSettingsPage();
    renderSidebar();
    toast(added.length
      ? `已从 D:\\app\\appearance 导入 ${result.count} 个主题，列表里可以切换`
      : `D:\\app\\appearance 里的 ${result.count} 个主题已经导入过了`);
  } catch (error) {
    toast('批量导入失败：' + error.message, 'error');
  }
}

async function deleteCustomTheme(file) {
  if (!file) return;
  const ok = await confirmDialog('删除自定义主题', '只会删除当前数据文件夹 appearance 目录里的这个 .js 文件，确定吗？', '删除', true);
  if (!ok) return;

  try {
    await call('deleteTheme', { file });
    const result = await call('listCustomThemes');
    await loadCustomThemes(result.files || []);
    if (!themeById(state.appearance.themeId)) {
      state.appearance.themeId = 'flat';
    }
    applyAppearance();
    renderSettingsPage();
    renderSidebar();
    toast('已删除主题');
  } catch (error) {
    toast('删除失败：' + error.message, 'error');
  }
}

/* ================= 提示与对话框 ================= */

function toast(message, type = 'info', action = null) {
  const element = document.createElement('div');
  element.className = 'toast' + (type === 'error' ? ' error' : '');

  const text = document.createElement('span');
  text.textContent = message;
  element.appendChild(text);

  if (action) {
    const button = document.createElement('button');
    button.type = 'button';
    button.textContent = action.label;
    button.addEventListener('click', () => {
      element.remove();
      action.onClick();
    });
    element.appendChild(button);
  }

  dom.toasts.appendChild(element);
  while (dom.toasts.children.length > 4) {
    dom.toasts.firstElementChild.remove();
  }

  requestAnimationFrame(() => element.classList.add('show'));
  setTimeout(() => {
    element.classList.remove('show');
    setTimeout(() => element.remove(), 240);
  }, action ? 6000 : 2600);
}

function confirmDialog(title, message, okText = '确定', danger = false) {
  return new Promise((resolve) => {
    state.modalResolve = resolve;
    dom.modalCard.innerHTML = `
      <h3>${escapeHtml(title)}</h3>
      <p>${escapeHtml(message)}</p>
      <div class="modal-actions">
        <button class="btn ghost" type="button" data-result="cancel">取消</button>
        <button class="btn ${danger ? 'danger' : 'primary'}" type="button" data-result="ok">${escapeHtml(okText)}</button>
      </div>`;
    dom.modalLayer.classList.remove('hidden');

    dom.modalCard.querySelector('[data-result="cancel"]').addEventListener('click', () => closeModal(false));
    dom.modalCard.querySelector('[data-result="ok"]').addEventListener('click', () => closeModal(true));
  });
}

function closeModal(result) {
  dom.modalLayer.classList.add('hidden');
  dom.modalCard.innerHTML = '';
  const resolve = state.modalResolve;
  state.modalResolve = null;
  if (resolve) resolve(result);
}
