// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// Served from GitHub Pages at https://bushero.github.io/GitSnapshotter/
export default defineConfig({
  site: 'https://bushero.github.io',
  base: '/GitSnapshotter',
  integrations: [
    starlight({
      title: 'GitSnapshotter',
      // The logo lives in assets/logo at the repository root; see also the favicon copy in package.json.
      logo: {
        light: '../assets/logo/icon-light.svg',
        dark: '../assets/logo/icon-dark.svg',
      },
      favicon: '/favicon.svg',
      customCss: ['./src/styles/custom.css'],
      description:
        "Snapshot a git repository's HEAD, branches, remotes, tags and working tree, and restore it later.",
      social: [
        { icon: 'github', label: 'GitHub', href: 'https://github.com/BusHero/GitSnapshotter' },
      ],
      editLink: {
        baseUrl: 'https://github.com/BusHero/GitSnapshotter/edit/trunk/docs/',
      },
      sidebar: [
        {
          label: 'Start here',
          items: ['introduction', 'installation', 'getting-started'],
        },
        {
          label: 'Library',
          items: [
            'library/taking-snapshots',
            'library/restoring',
            'library/choosing-what-to-restore',
            'library/working-tree',
            'library/testing',
            'library/saving-and-loading',
            'library/comparing-snapshots',
            'library/error-handling',
          ],
        },
        {
          label: 'Command line',
          items: [
            'cli/overview',
            'cli/snapshot',
            'cli/restore',
            'cli/diff',
            'cli/scripting',
          ],
        },
        {
          label: 'Concepts',
          items: ['concepts/how-it-works', 'concepts/snapshot-format', 'concepts/limitations'],
        },
        {
          label: 'API reference',
          items: [
            'reference/gitrepository',
            'reference/options',
            'reference/snapshot-types',
            'reference/snapshot-diff',
            'reference/exceptions',
          ],
        },
      ],
    }),
  ],
});
