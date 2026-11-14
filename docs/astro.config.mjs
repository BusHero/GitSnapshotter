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
          items: [
            { label: 'Introduction', slug: 'introduction' },
            { label: 'Getting started', slug: 'getting-started' },
          ],
        },
        { label: 'Guides', items: [{ autogenerate: { directory: 'guides' } }] },
        { label: 'Reference', items: [{ autogenerate: { directory: 'reference' } }] },
      ],
    }),
  ],
});
