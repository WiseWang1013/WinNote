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

const state = {
  notes: [],
  settings: { theme: 'light' },
  view: { type: 'all', category: null },
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
  modalResolve: null,
  draw: { color: '#1A1A1C', width: 2.5, drawing: false, last: null, dpr: 1 },
};

const dom = {};
const pendingCalls = new Map();
let callSeq = 0;

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
  buildCategories();
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
    state.settings = Object.assign({ theme: 'light' }, data.settings || {});
    applyTheme(state.settings.theme, false);
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
    'app', 'btnNew', 'btnImport', 'nav', 'countAll', 'countPinned', 'countTrash', 'cats',
    'btnTheme', 'themeIcon', 'themeLabel', 'btnData', 'viewTitle', 'viewCount',
    'btnEmptyTrash', 'searchInput', 'btnClearSearch', 'noteList', 'listEmpty',
    'listEmptyText', 'btnNew2', 'editorPane', 'editorEmpty', 'btnNew3', 'editor',
    'noteTime', 'noteWords', 'saveState', 'btnCategoryChip', 'catChipDot',
    'catChipText', 'btnPin', 'btnColor', 'btnExport', 'btnMore', 'editorScroll', 'titleInput',
    'body', 'toolbar', 'btnVoice', 'voiceBar', 'voiceTitle', 'voiceHypo',
    'voiceStop', 'popoverLayer', 'toasts', 'modalLayer', 'modalCard', 'drawLayer',
    'drawClose', 'drawCanvas', 'penColors', 'penWidths', 'drawClear',
    'drawCancel', 'drawInsert', 'printRoot',
  ];
  for (const id of ids) dom[id] = document.getElementById(id);
}

function buildCategories() {
  dom.cats.innerHTML = CATEGORIES.map(
    (c) => `<button class="cat-item" data-cat="${c.id}" type="button">
      <i style="--dot:${c.color}"></i><span>${c.name}</span><em data-count="${c.id}">0</em>
    </button>`
  ).join('');
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

  dom.cats.addEventListener('click', async (event) => {
    const button = event.target.closest('.cat-item');
    if (!button) return;
    await setView('category', button.dataset.cat);
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
  });

  dom.btnPin.addEventListener('click', togglePin);
  dom.btnColor.addEventListener('click', () => openColorPopover(dom.btnColor));
  dom.btnExport.addEventListener('click', () => openExportPopover(dom.btnExport));
  dom.btnCategoryChip.addEventListener('click', () => openCategoryPopover(dom.btnCategoryChip));
  dom.btnMore.addEventListener('click', () => openMorePopover(dom.btnMore));

  dom.editorScroll.addEventListener('scroll', closePopover);

  dom.voiceStop.addEventListener('click', () => stopVoice());

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
  dom.penWidths.addEventListener('click', (event) => {
    const button = event.target.closest('.pen-width');
    if (!button) return;
    dom.penWidths.querySelectorAll('.pen-width').forEach((b) => b.classList.remove('active'));
    button.classList.add('active');
    state.draw.width = parseFloat(button.dataset.width);
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
    dom.searchInput.focus();
    dom.searchInput.select();
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
  return (note.text || '').replace(/\s+/g, ' ').trim().slice(0, 110);
}

function catName(id) {
  const category = CATEGORIES.find((c) => c.id === id);
  return category ? category.name : '';
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
  if (state.view.type === 'category') return catName(state.view.category) || '分类';
  return '全部笔记';
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
  }

  const query = state.query.trim().toLowerCase();
  if (query) {
    list = list.filter((note) =>
      `${note.title}\n${note.text}`.toLowerCase().includes(query)
    );
  }

  if (state.view.type === 'trash') {
    list.sort((a, b) => new Date(b.deletedAt || 0) - new Date(a.deletedAt || 0));
  } else {
    list.sort((a, b) => {
      if (a.pinned !== b.pinned) return a.pinned ? -1 : 1;
      return new Date(b.updatedAt || 0) - new Date(a.updatedAt || 0);
    });
  }
  return list;
}

/* ================= 渲染 ================= */

function renderSidebar() {
  const alive = state.notes.filter((note) => !note.deleted);
  dom.countAll.textContent = alive.length;
  dom.countPinned.textContent = alive.filter((note) => note.pinned).length;
  dom.countTrash.textContent = state.notes.filter((note) => note.deleted).length;

  for (const category of CATEGORIES) {
    const counter = dom.cats.querySelector(`em[data-count="${category.id}"]`);
    if (counter) {
      counter.textContent = alive.filter((note) => note.category === category.id).length;
    }
  }

  dom.nav.querySelectorAll('.nav-item').forEach((button) => {
    button.classList.toggle('active', state.view.type === button.dataset.view);
  });
  dom.cats.querySelectorAll('.cat-item').forEach((button) => {
    button.classList.toggle(
      'active',
      state.view.type === 'category' && state.view.category === button.dataset.cat
    );
  });
}

function renderList() {
  const notes = visibleNotes();
  const trash = state.view.type === 'trash';
  dom.viewTitle.textContent = state.query.trim() ? '搜索结果' : viewTitle();
  dom.viewCount.textContent = notes.length;
  dom.btnEmptyTrash.classList.toggle('hidden', !trash || notes.length === 0);

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
  if (state.view.type === 'category') return '这个分类里还没有笔记';
  if (state.view.type === 'pinned') return '还没有置顶的备忘录';
  return '还没有备忘录';
}

function cardHtml(note, trash) {
  const colors = colorOf(note.color);
  const title = escapeHtml(note.title.trim() || '无标题');
  const snippet = escapeHtml(noteSnippet(note)) || '空白备忘录';
  const pin = note.pinned
    ? '<svg class="icon pin-flag"><use href="#i-pin"/></svg>'
    : '';
  const image = note.images && note.images.length
    ? '<svg class="icon"><use href="#i-image"/></svg>'
    : '';
  const todo = /todo-item/.test(note.html || '')
    ? '<svg class="icon"><use href="#i-todo"/></svg>'
    : '';

  const foot = trash
    ? `<span class="card-date">删除于 ${fmtDate(note.deletedAt)}</span>
       <span class="del-actions">
         <button class="mini-btn" data-act="restore" type="button">恢复</button>
         <button class="mini-btn danger" data-act="purge" type="button">彻底删除</button>
       </span>`
    : `<span class="card-date">${fmtDate(note.updatedAt)}</span>
       <span class="card-icons">${pin}${image}${todo}</span>`;

  return `<article class="note-card ${trash ? 'trash' : ''} ${note.id === state.currentId ? 'active' : ''}"
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
  if (title) title.textContent = note.title.trim() || '无标题';
  if (snippet) snippet.textContent = noteSnippet(note) || '空白备忘录';
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
    renderList();
    return;
  }
  if (state.currentNote) await flushSave();
  if (state.voiceOn) await stopVoice();

  const note = state.notes.find((item) => item.id === id && !item.deleted);
  if (!note) {
    showEditorEmpty();
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

  dom.editor.classList.remove('hidden');
  dom.editorEmpty.classList.add('hidden');
  applyEditorColor(note);
  syncPinButton();
  syncCategoryChip();
  updateEditorMeta();

  dom.editorScroll.scrollTop = 0;
  renderList();
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
}

async function newNote() {
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
  };

  state.notes.unshift(note);
  if (state.view.type === 'trash') state.view = { type: 'all', category: null };
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
  };

  state.notes.unshift(note);
  state.view = { type: 'all', category: null };
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

async function setView(type, category = null) {
  if (state.currentNote) await flushSave();
  state.view = { type, category };
  renderSidebar();
  renderList();

  const visible = visibleNotes();
  if (state.currentNote && !visible.some((note) => note.id === state.currentId)) {
    showEditorEmpty();
  }
}

/* ================= 保存 ================= */

function collectCurrent() {
  const note = state.currentNote;
  if (!note) return;
  note.title = dom.titleInput.value;
  note.html = dom.body.innerHTML;
  note.text = extractPlainText();
  note.images = Array.from(dom.body.querySelectorAll('figure[data-file]')).map(
    (figure) => figure.dataset.file
  );
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
  dom.editorPane.style.background = colors.bg;
}

function syncPinButton() {
  const pinned = !!state.currentNote?.pinned;
  dom.btnPin.classList.toggle('active', pinned);
  dom.btnPin.title = pinned ? '取消置顶' : '置顶';
}

function syncCategoryChip() {
  const category = CATEGORIES.find((c) => c.id === state.currentNote?.category);
  dom.catChipText.textContent = category ? category.name : '未分类';
  dom.catChipDot.style.background = category ? category.color : 'var(--text-3)';
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
  const items = CATEGORIES.map((category) => `
    <button class="pop-item" type="button" data-cat="${category.id}">
      <i style="width:8px;height:8px;border-radius:50%;background:${category.color}"></i>
      <span>${category.name}</span>
      ${current === category.id ? '<svg class="icon check"><use href="#i-check"/></svg>' : ''}
    </button>`).join('');

  const popover = mountPopover(
    `<div class="pop-title">移动到分类</div>${items}
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

  dom.voiceHypo.textContent = '正在启动语音识别…';
  dom.voiceTitle.textContent = '正在聆听…';
  dom.voiceBar.classList.remove('hidden');
  dom.btnVoice.classList.add('active');

  try {
    await call('voiceStart');
  } catch (error) {
    setVoiceUi(false);
    toast(error.message, 'error');
  }
}

async function stopVoice() {
  if (!state.voiceOn) {
    setVoiceUi(false);
    return;
  }
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

  dom.body.focus();
  const selection = window.getSelection();
  let range = state.voiceRange;

  const live = selection.rangeCount ? selection.getRangeAt(0) : null;
  if (live && dom.body.contains(live.startContainer)) {
    range = live;
  }

  if (range && dom.body.contains(range.startContainer)) {
    selection.removeAllRanges();
    selection.addRange(range);
  } else {
    placeCaretAtEnd(dom.body);
  }

  document.execCommand('insertText', false, text);
  state.voiceRange = saveSelection();
  afterBodyEdit();
}

function handleNativeEvent(name, data) {
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

function bindCanvas() {
  const canvas = dom.drawCanvas;

  canvas.addEventListener('pointerdown', (event) => {
    if (event.button !== 0) return;
    canvas.setPointerCapture(event.pointerId);
    state.draw.drawing = true;
    const point = canvasPoint(event);
    state.draw.last = point;

    const ctx = canvas.getContext('2d');
    ctx.fillStyle = state.draw.color;
    ctx.beginPath();
    ctx.arc(point.x, point.y, state.draw.width / 2, 0, Math.PI * 2);
    ctx.fill();
  });

  canvas.addEventListener('pointermove', (event) => {
    if (!state.draw.drawing) return;
    const point = canvasPoint(event);
    const ctx = canvas.getContext('2d');
    ctx.strokeStyle = state.draw.color;
    ctx.lineWidth = state.draw.width;
    ctx.beginPath();
    ctx.moveTo(state.draw.last.x, state.draw.last.y);
    ctx.lineTo(point.x, point.y);
    ctx.stroke();
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

/* ================= 主题 ================= */

function applyTheme(theme, persist) {
  theme = theme === 'dark' ? 'dark' : 'light';
  state.settings.theme = theme;
  document.body.dataset.theme = theme;
  updateThemeButton();

  if (state.currentNote) {
    applyEditorColor(state.currentNote);
  }
  renderList();

  if (persist) {
    call('setTheme', { theme }).catch(() => {});
  }
}

function updateThemeButton() {
  const dark = state.settings.theme === 'dark';
  dom.themeLabel.textContent = dark ? '浅色模式' : '深色模式';
  dom.themeIcon.setAttribute('href', dark ? '#i-sun' : '#i-moon');
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
