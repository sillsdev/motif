import starlight from '@astrojs/starlight';
import starlightLlmsTxt from 'starlight-llms-txt';
import { defineConfig } from 'astro/config';

export default defineConfig({
	site: 'https://motif-docs.pages.dev',
	trailingSlash: 'always',
	integrations: [
		starlight({
			title: 'Motif',
			description: 'Guides, command help, and developer documentation for Motif.',
			defaultLocale: 'root',
			locales: {
				root: { label: 'English', lang: 'en' },
			},
			sidebar: [
				{
					label: 'Guide',
					items: [
						{ autogenerate: { directory: 'guide' } },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'Commands', items: [{ autogenerate: { directory: 'reference/commands' } }] },
						{ label: 'Terms', items: [{ autogenerate: { directory: 'reference/terms' } }] },
						{ label: 'Controls', items: [{ autogenerate: { directory: 'reference/controls' } }] },
						{ label: 'API', items: [{ autogenerate: { directory: 'reference/api' } }] },
					],
				},
				{
					label: 'Developers',
					items: [{ autogenerate: { directory: 'developers' } }],
				},
			],
			customCss: ['./src/styles/site.css'],
			plugins: [starlightLlmsTxt()],
		}),
	],
});
