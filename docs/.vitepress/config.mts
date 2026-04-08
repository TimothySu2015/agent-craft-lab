import { defineConfig } from 'vitepress';

const enSidebar = [
  {
    text: 'User Guide',
    items: [
      { text: 'Getting Started', link: '/en/user-guide/getting-started' },
      { text: 'Studio Canvas', link: '/en/user-guide/studio' },
      { text: 'Node Types', link: '/en/user-guide/nodes' },
      { text: 'Tools & RAG', link: '/en/user-guide/tools-and-rag' },
      { text: 'Execution & Publishing', link: '/en/user-guide/execution-and-publishing' },
      { text: 'Database Providers', link: '/en/user-guide/database-providers' },
      { text: 'DocRefinery', link: '/en/user-guide/doc-refinery' },
    ],
  },
  {
    text: 'Developer Guide',
    items: [
      { text: 'Architecture', link: '/en/developer-guide/architecture' },
      { text: 'Extension Guide', link: '/en/developer-guide/extending' },
      { text: 'API Reference', link: '/en/developer-guide/api-reference' },
    ],
  },
];

const zhTWSidebar = [
  {
    text: '使用指南',
    items: [
      { text: '快速入門', link: '/zh-TW/user-guide/getting-started' },
      { text: 'Studio 畫布', link: '/zh-TW/user-guide/studio' },
      { text: '節點類型', link: '/zh-TW/user-guide/nodes' },
      { text: '工具與 RAG', link: '/zh-TW/user-guide/tools-and-rag' },
      { text: '執行與發布', link: '/zh-TW/user-guide/execution-and-publishing' },
      { text: '資料庫 Provider', link: '/zh-TW/user-guide/database-providers' },
      { text: 'DocRefinery', link: '/zh-TW/user-guide/doc-refinery' },
    ],
  },
  {
    text: '開發者指南',
    items: [
      { text: '系統架構', link: '/zh-TW/developer-guide/architecture' },
      { text: '擴充指南', link: '/zh-TW/developer-guide/extending' },
      { text: 'API 參考', link: '/zh-TW/developer-guide/api-reference' },
    ],
  },
];

const jaSidebar = [
  {
    text: 'ユーザーガイド',
    items: [
      { text: 'はじめに', link: '/ja/user-guide/getting-started' },
      { text: 'Studio キャンバス', link: '/ja/user-guide/studio' },
      { text: 'ノードタイプ', link: '/ja/user-guide/nodes' },
      { text: 'ツールと RAG', link: '/ja/user-guide/tools-and-rag' },
      { text: '実行と公開', link: '/ja/user-guide/execution-and-publishing' },
      { text: 'データベース Provider', link: '/ja/user-guide/database-providers' },
      { text: 'DocRefinery', link: '/ja/user-guide/doc-refinery' },
    ],
  },
  {
    text: '開発者ガイド',
    items: [
      { text: 'アーキテクチャ', link: '/ja/developer-guide/architecture' },
      { text: '拡張ガイド', link: '/ja/developer-guide/extending' },
      { text: 'API リファレンス', link: '/ja/developer-guide/api-reference' },
    ],
  },
];

export default defineConfig({
  title: 'AgentCraftLab',
  description: 'Open-source .NET AI Agent workflow framework',
  base: '/agent-craft-lab/',
  lastUpdated: true,
  head: [['link', { rel: 'icon', href: '/agent-craft-lab/favicon.ico' }]],
  ignoreDeadLinks: [/localhost/],

  locales: {
    en: {
      label: 'English',
      lang: 'en',
      themeConfig: {
        nav: [
          { text: 'User Guide', link: '/en/user-guide/getting-started' },
          { text: 'Developer Guide', link: '/en/developer-guide/architecture' },
        ],
        sidebar: {
          '/en/': enSidebar,
        },
      },
    },
    'zh-TW': {
      label: '繁體中文',
      lang: 'zh-TW',
      themeConfig: {
        nav: [
          { text: '使用指南', link: '/zh-TW/user-guide/getting-started' },
          { text: '開發者指南', link: '/zh-TW/developer-guide/architecture' },
        ],
        sidebar: {
          '/zh-TW/': zhTWSidebar,
        },
        lastUpdated: { text: '最後更新' },
        outline: { label: '目錄' },
        docFooter: { prev: '上一頁', next: '下一頁' },
      },
    },
    ja: {
      label: '日本語',
      lang: 'ja',
      themeConfig: {
        nav: [
          { text: 'ユーザーガイド', link: '/ja/user-guide/getting-started' },
          { text: '開発者ガイド', link: '/ja/developer-guide/architecture' },
        ],
        sidebar: {
          '/ja/': jaSidebar,
        },
        lastUpdated: { text: '最終更新' },
        outline: { label: '目次' },
        docFooter: { prev: '前のページ', next: '次のページ' },
      },
    },
  },

  themeConfig: {
    logo: '/logo.svg',
    socialLinks: [
      { icon: 'github', link: 'https://github.com/TimothySu2015/agent-craft-lab' },
    ],
    search: {
      provider: 'local',
    },
    editLink: {
      pattern: 'https://github.com/TimothySu2015/agent-craft-lab/edit/main/docs/:path',
    },
    footer: {
      message: 'Released under the BSL-1.1 License.',
      copyright: 'Copyright 2025-present AgentCraftLab Contributors',
    },
    outline: {
      level: [2, 3],
    },
  },
});
