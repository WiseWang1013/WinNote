'use strict';

/* WinNote 3.0 外观主题工具。
 * 内置主题随 exe 打包；自定义主题是 data/appearance 下的 .js 文件。
 * 自定义主题写法：
 *   WinNote.registerTheme({
 *     id: 'acrylic', name: '亚克力', description: '...',
 *     light: { bg: '#...', panel: '#...', ... },
 *     dark: { ... },
 *     css: '...' // 可选，追加到页面的 CSS
 *   });
 */
(function () {
  const flatLight = {
    bg: '#F2F3F5', panel: '#FFFFFF', panel2: '#F7F8FA',
    text: '#1A1A1C', text2: '#55575E', text3: '#7B7D85',
    line: '#E5E7EC', lineStrong: '#D6D9E0',
    hover: 'rgba(0,0,0,.045)', active: 'rgba(0,0,0,.075)',
    danger: '#E8402A', dangerSoft: 'rgba(232,64,42,.09)',
    searchBg: '#F2F3F6', cardLine: 'rgba(0,0,0,.045)',
    cardShadow: '0 1px 2px rgba(20,24,40,.05)',
    cardShadowHover: '0 8px 20px rgba(20,24,40,.10)',
    shadowLg: '0 16px 44px rgba(20,24,40,.17)',
    toolbarShadow: '0 10px 30px rgba(20,24,40,.14)',
    scrollThumb: 'rgba(0,0,0,.16)',
    appBg: '#F2F3F5', panelSolid: '#FFFFFF', glassBlur: '0px',
  };

  const flatDark = {
    bg: '#0E0F12', panel: '#17181C', panel2: '#141519',
    text: '#ECEDEF', text2: '#B4B6BD', text3: '#8D9097',
    line: '#26282E', lineStrong: '#34373E',
    hover: 'rgba(255,255,255,.06)', active: 'rgba(255,255,255,.09)',
    danger: '#FF6B5A', dangerSoft: 'rgba(255,107,90,.14)',
    searchBg: '#1E2025', cardLine: 'rgba(255,255,255,.06)',
    cardShadow: '0 1px 2px rgba(0,0,0,.3)',
    cardShadowHover: '0 10px 26px rgba(0,0,0,.45)',
    shadowLg: '0 20px 60px rgba(0,0,0,.55)',
    toolbarShadow: '0 12px 34px rgba(0,0,0,.5)',
    scrollThumb: 'rgba(255,255,255,.18)',
    appBg: '#0E0F12', panelSolid: '#17181C', glassBlur: '0px',
  };

  const skeuoLight = {
    bg: '#EFE8DA', panel: '#FFFBF2', panel2: '#F6EEDF',
    text: '#2B241A', text2: '#655846', text3: '#8A7861',
    line: '#E3D4BC', lineStrong: '#D1BC99',
    hover: 'rgba(92,67,32,.07)', active: 'rgba(92,67,32,.12)',
    danger: '#C9432F', dangerSoft: 'rgba(201,67,47,.1)',
    searchBg: '#F2E7D3', cardLine: 'rgba(94,66,27,.08)',
    cardShadow: 'inset 0 1px 0 rgba(255,255,255,.85), 0 3px 7px rgba(76,52,18,.12)',
    cardShadowHover: 'inset 0 1px 0 rgba(255,255,255,.9), 0 8px 20px rgba(76,52,18,.18)',
    shadowLg: '0 18px 50px rgba(64,44,18,.22)',
    toolbarShadow: '0 12px 30px rgba(64,44,18,.18)',
    scrollThumb: 'rgba(90,65,30,.22)',
    appBg: 'radial-gradient(1100px 700px at 15% -10%, #FFF9EC 0%, #EFE8DA 55%, #E7DECE 100%)',
    panelSolid: '#FFFBF2', glassBlur: '0px',
  };

  const skeuoDark = {
    bg: '#1C1812', panel: '#2A2319', panel2: '#241E16',
    text: '#F3EBDD', text2: '#CDBFA9', text3: '#9E8C74',
    line: '#3A3022', lineStrong: '#4B3D2B',
    hover: 'rgba(255,235,200,.07)', active: 'rgba(255,235,200,.12)',
    danger: '#FF7A63', dangerSoft: 'rgba(255,122,99,.14)',
    searchBg: '#2F271C', cardLine: 'rgba(255,226,180,.08)',
    cardShadow: 'inset 0 1px 0 rgba(255,255,255,.06), 0 4px 10px rgba(0,0,0,.34)',
    cardShadowHover: 'inset 0 1px 0 rgba(255,255,255,.08), 0 12px 30px rgba(0,0,0,.5)',
    shadowLg: '0 24px 60px rgba(0,0,0,.55)',
    toolbarShadow: '0 14px 34px rgba(0,0,0,.5)',
    scrollThumb: 'rgba(255,226,180,.2)',
    appBg: 'radial-gradient(1100px 700px at 15% -10%, #332A1D 0%, #1C1812 60%, #15120D 100%)',
    panelSolid: '#2A2319', glassBlur: '0px',
  };

  const minimalLight = {
    bg: '#FAFAF9', panel: '#FFFFFF', panel2: '#F7F7F5',
    text: '#111214', text2: '#5E6268', text3: '#8B8F95',
    line: '#E9E9E6', lineStrong: '#D9D9D5',
    hover: 'rgba(0,0,0,.035)', active: 'rgba(0,0,0,.06)',
    danger: '#D63B2F', dangerSoft: 'rgba(214,59,47,.08)',
    searchBg: '#F4F4F1', cardLine: 'rgba(0,0,0,.03)',
    cardShadow: 'none', cardShadowHover: '0 4px 14px rgba(0,0,0,.06)',
    shadowLg: '0 12px 30px rgba(0,0,0,.1)',
    toolbarShadow: '0 8px 20px rgba(0,0,0,.08)',
    scrollThumb: 'rgba(0,0,0,.12)',
    appBg: '#FAFAF9', panelSolid: '#FFFFFF', glassBlur: '0px',
  };

  const minimalDark = {
    bg: '#0D0D0E', panel: '#141416', panel2: '#101012',
    text: '#F1F1EF', text2: '#B7B8BA', text3: '#888A8D',
    line: '#242427', lineStrong: '#343438',
    hover: 'rgba(255,255,255,.05)', active: 'rgba(255,255,255,.08)',
    danger: '#FF6B5A', dangerSoft: 'rgba(255,107,90,.12)',
    searchBg: '#1A1A1D', cardLine: 'rgba(255,255,255,.04)',
    cardShadow: 'none', cardShadowHover: '0 6px 18px rgba(0,0,0,.4)',
    shadowLg: '0 16px 40px rgba(0,0,0,.5)',
    toolbarShadow: '0 10px 24px rgba(0,0,0,.45)',
    scrollThumb: 'rgba(255,255,255,.15)',
    appBg: '#0D0D0E', panelSolid: '#141416', glassBlur: '0px',
  };

  const glassLight = {
    bg: '#DDEAF7', panel: 'rgba(255,255,255,.32)', panel2: 'rgba(255,255,255,.16)',
    text: '#122033', text2: '#40536C', text3: '#6F829A',
    line: 'rgba(255,255,255,.62)', lineStrong: 'rgba(255,255,255,.82)',
    hover: 'rgba(255,255,255,.3)', active: 'rgba(255,255,255,.48)',
    danger: '#D94132', dangerSoft: 'rgba(217,65,50,.1)',
    searchBg: 'rgba(255,255,255,.32)', cardLine: 'rgba(255,255,255,.62)',
    cardShadow: '0 8px 24px rgba(49,78,118,.15), inset 0 1px 0 rgba(255,255,255,.82)',
    cardShadowHover: '0 18px 38px rgba(49,78,118,.24), inset 0 1px 0 rgba(255,255,255,.95)',
    shadowLg: '0 28px 70px rgba(39,68,106,.28)',
    toolbarShadow: '0 18px 40px rgba(39,68,106,.24)',
    scrollThumb: 'rgba(39,68,106,.18)',
    appBg: 'radial-gradient(52% 56% at 10% 4%, #8BC6FF 0%, transparent 62%), radial-gradient(48% 58% at 92% 8%, #D8A9FF 0%, transparent 66%), radial-gradient(56% 62% at 52% 98%, #8FF1D0 0%, transparent 66%), linear-gradient(135deg, #D9EDFF 0%, #F0E7FF 52%, #DDF9EE 100%)',
    panelSolid: '#E7F1FF', glassBlur: '18px',
  };

  const glassDark = {
    bg: '#080F1D', panel: 'rgba(28,36,52,.4)', panel2: 'rgba(30,38,54,.22)',
    text: '#F4F8FF', text2: '#BAC7DB', text3: '#8494AB',
    line: 'rgba(255,255,255,.14)', lineStrong: 'rgba(255,255,255,.22)',
    hover: 'rgba(255,255,255,.075)', active: 'rgba(255,255,255,.12)',
    danger: '#FF7B6B', dangerSoft: 'rgba(255,123,107,.14)',
    searchBg: 'rgba(255,255,255,.06)', cardLine: 'rgba(255,255,255,.14)',
    cardShadow: '0 10px 28px rgba(0,0,0,.34), inset 0 1px 0 rgba(255,255,255,.14)',
    cardShadowHover: '0 20px 44px rgba(0,0,0,.46), inset 0 1px 0 rgba(255,255,255,.2)',
    shadowLg: '0 30px 80px rgba(0,0,0,.56)',
    toolbarShadow: '0 20px 46px rgba(0,0,0,.5)',
    scrollThumb: 'rgba(255,255,255,.17)',
    appBg: 'radial-gradient(52% 56% at 10% 4%, #123C74 0%, transparent 62%), radial-gradient(48% 58% at 92% 8%, #4A1F74 0%, transparent 66%), radial-gradient(56% 62% at 52% 98%, #0C4D43 0%, transparent 66%), linear-gradient(135deg, #070D18 0%, #0B1324 52%, #0A1A21 100%)',
    panelSolid: '#101827', glassBlur: '20px',
  };

  const builtinThemes = [
    {
      id: 'flat', name: '平面', subtitle: 'Flat', description: '干净利落的纯色表面，默认的轻量外观。',
      swatch: ['#F2F3F5', '#FFFFFF', '#0A59F7'], light: flatLight, dark: flatDark,
    },
    {
      id: 'skeuo', name: '拟物', subtitle: 'Skeuomorphic', description: '暖纸、浮雕和轻微纹理，像真实的桌面便签。',
      swatch: ['#EFE8DA', '#FFFBF2', '#B07A32'], light: skeuoLight, dark: skeuoDark,
    },
    {
      id: 'minimal', name: '简约', subtitle: 'Minimal', description: '更少的线、更轻的阴影，专注文字本身。',
      swatch: ['#FAFAF9', '#FFFFFF', '#222426'], light: minimalLight, dark: minimalDark,
    },
    {
      id: 'glass', name: '液态玻璃', subtitle: 'Liquid Glass', description: '半透明玻璃、流动渐变和柔和高光。',
      swatch: ['#DCEEFF', 'rgba(255,255,255,.56)', '#8B5CF6'], light: glassLight, dark: glassDark,
    },
  ];

  const accents = [
    { id: 'blue', name: '蓝', light: '#0A59F7', dark: '#4C8DFF' },
    { id: 'sky', name: '浅蓝', light: '#0284C7', dark: '#38BDF8' },
    { id: 'mint', name: '浅绿', light: '#0F9D6B', dark: '#34D399' },
    { id: 'lilac', name: '薰衣草', light: '#7C3AED', dark: '#A78BFA' },
    { id: 'rose', name: '玫瑰', light: '#E11D48', dark: '#FB7185' },
    { id: 'amber', name: '琥珀', light: '#C27603', dark: '#FBBF24' },
    { id: 'graphite', name: '石墨', light: '#475569', dark: '#94A3B8' },
  ];

  const radiusPresets = [
    { id: 'square', name: '直角', description: '硬朗、克制', radius: '2px', sm: '2px', lg: '3px', xl: '4px', corner: 'round' },
    { id: 'soft', name: '小圆角', description: '轻微收边', radius: '8px', sm: '5px', lg: '12px', xl: '14px', corner: 'round' },
    { id: 'round', name: '圆角', description: '常规柔和', radius: '14px', sm: '9px', lg: '20px', xl: '24px', corner: 'round' },
    { id: 'large', name: '大圆角', description: '更饱满的卡片', radius: '22px', sm: '14px', lg: '30px', xl: '36px', corner: 'round' },
    { id: 'squircle', name: '苹果连续圆角', description: '连续曲率，默认', radius: '18px', sm: '12px', lg: '26px', xl: '30px', corner: 'superellipse(4)' },
    { id: 'superround', name: '超圆', description: '胶囊感', radius: '34px', sm: '22px', lg: '46px', xl: '54px', corner: 'round' },
  ];

  window.__WINNOTE_CUSTOM_THEMES__ = window.__WINNOTE_CUSTOM_THEMES__ || {};
  window.WinNote = window.WinNote || {};
  window.WinNote.registerTheme = function (theme) {
    if (!theme || !theme.id) return;
    const id = String(theme.id);
    window.__WINNOTE_CUSTOM_THEMES__[id] = Object.assign({ custom: true }, theme, { id });
  };

  window.WinNoteThemeKit = {
    builtinThemes,
    accents,
    radiusPresets,
    allBuiltins: () => builtinThemes.slice(),
    getAccent: (id) => accents.find((accent) => accent.id === id) || accents[0],
    getRadius: (id) => radiusPresets.find((radius) => radius.id === id) || radiusPresets[2],
    getCustomThemes: () => Object.values(window.__WINNOTE_CUSTOM_THEMES__ || {}),
  };
})();
