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
			head: [
				{ tag: 'link', attrs: { rel: 'preconnect', href: 'https://fonts.googleapis.com' } },
				{ tag: 'link', attrs: { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' } },
				{
					tag: 'link',
					attrs: {
						rel: 'stylesheet',
						href: 'https://fonts.googleapis.com/css2?family=Noto+Sans:ital,wght@0,400;0,600;0,700;0,800;1,400&family=IBM+Plex+Mono:wght@400;500&display=swap',
					},
				},
			],
			defaultLocale: 'root',
			locales: {
				root: { label: 'English', lang: 'en' },
			},
			sidebar: [
				{ label: 'Samples', link: '/samples/' },
				{
					label: 'Guide',
					items: [
						{ autogenerate: { directory: 'guide' } },
					],
				},
				{
					label: 'Learn',
					items: [{ autogenerate: { directory: 'learn' } }],
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
					items: [
						{ label: 'How parser correctness is checked', link: '/parser-correctness/' },
						{ autogenerate: { directory: 'developers' } },
					],
				},
			],
			components: {
				SiteTitle: './src/components/home/HomeSiteTitle.astro',
				SocialIcons: './src/components/home/HomePrimaryNav.astro',
				Sidebar: './src/components/home/HomeLearnSidebar.astro',
				PageTitle: './src/components/home/HomePageTitle.astro',
				MarkdownContent: './src/components/home/LearnLessonContent.astro',
				Footer: './src/components/home/HomeFooter.astro',
			},
			customCss: ['./src/styles/site.css'],
			plugins: [starlightLlmsTxt()],
		}),
	],
});
