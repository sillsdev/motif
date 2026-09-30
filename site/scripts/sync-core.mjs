import { access, copyFile, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { convertXmlDocsToMarkdown } from '../../tools/xml-docs-to-markdown.mjs';

const guideSections = [
	{
		title: 'Get started',
		pages: ['what-is-motif', 'install', 'open-a-project', 'first-run-setup', 'reading-the-overview'],
	},
	{
		title: 'Pages of the window',
		pages: ['overview', 'texts', 'try-a-word', 'timing', 'warnings', 'review-changes', 'ai-handoff'],
	},
	{
		title: 'Everyday tasks',
		pages: ['refresh-numbers', 'change-an-analysis', 'replace-a-pending-change', 'apply-to-fieldworks', 'when-a-change-no-longer-fits', 'switch-projects', 'cancel-a-long-run', 'when-something-goes-wrong'],
	},
	{
		title: 'Concepts',
		pages: ['baseline', 'assessment', 'pangloss', 'text-coverage', 'default-selection', 'drift', 'pending-changes'],
	},
	{
		title: 'For AI agents and scripts',
		pages: ['agents/start-here', 'agents/output-and-exit-codes', 'agents/measure-a-grammar', 'agents/work-with-jobs', 'agents/handoff'],
	},
];
const guideCodes = guideSections.flatMap((section) => section.pages);
const guideOrder = new Map(guideCodes.map((code, index) => [code, index + 1]));
const learnSidebarOrder = [
	'index',
	'what-the-parser-knows',
	'stems-and-the-lexicon',
	'affixes-slots-and-templates',
	'allomorphs-and-environments',
	'phonological-rules',
	'modelling-a-grammar-the-parser-can-use',
	'reading-why-a-word-fails',
	'why-a-grammar-is-slow',
	'working-with-an-ai-consultant',
];
const learnOrderByCode = new Map(learnSidebarOrder.map((slug, index) => [`learn/${slug}`, index]));
const homeFeatureCodes = ['overview', 'texts', 'try-a-word', 'timing', 'warnings', 'review-changes', 'ai-handoff'];
const sampleIdByLearnPrefix = new Map([['turkish', 'synthetic-turkic']]);

function yamlString(value) {
	return JSON.stringify(value ?? '');
}

function frontmatter(title, description, sidebarOrder) {
	const sidebar = sidebarOrder === undefined ? '' : `sidebar:\n  order: ${sidebarOrder}\n`;
	return `---\ntitle: ${yamlString(title)}\ndescription: ${yamlString(description)}\n${sidebar}---\n\n`;
}

function slugify(value) {
	return value.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

function markdownTitle(markdown, fallback) {
	const title = markdown.match(/^#\s+(.+)$/m);
	return title?.[1].trim() ?? fallback;
}

function markdownDescription(markdown, fallback) {
	const body = markdown
		.replace(/^---\r?\n[\s\S]*?\r?\n---\s*\r?\n/, '')
		.replace(/<!--([\s\S]*?)-->/g, '')
		.replace(/^#+\s+.*$/gm, '')
		.trim();
	const paragraph = body.split(/\r?\n\s*\r?\n/).find(Boolean);
	return (paragraph ?? fallback).replace(/\s+/g, ' ').slice(0, 180);
}

function stripFrontmatterAndComments(markdown) {
	return markdown
		.replace(/^---\r?\n[\s\S]*?\r?\n---\s*\r?\n/, '')
		.replace(/<!--([\s\S]*?)-->/g, '')
		.trim();
}

function stripDuplicateTitle(markdown, title) {
	const match = markdown.match(/^#\s+([^\r\n]+)\r?\n?/);
	return match?.[1].trim() === title ? markdown.slice(match[0].length).trimStart() : markdown;
}

function commandMetadata(entry) {
	const lines = [];
	if (entry.surface) lines.push(`**Surface: ${entry.surface}**`);
	if (Array.isArray(entry.usage) && entry.usage.length) {
		lines.push('## Usage', ...entry.usage.map((usage) => `- \`${usage.replaceAll('`', '\\`')}\``));
	}
	return lines.length ? `${lines.join('\n\n')}\n\n` : '';
}

function entryUrlPath(entry) {
	return new URL(entry.url).pathname;
}

function entrySiteRoute(entry, siteRoot, locale) {
	let url;
	try {
		url = new URL(entry.url);
	} catch {
		throw new Error(`Invalid help entry URL: ${entry.kind} ${entry.code}`);
	}
	if (url.origin !== new URL(siteRoot).origin || url.search || url.hash || !url.pathname.endsWith('/')) {
		throw new Error(`Invalid help entry URL: ${entry.kind} ${entry.code}`);
	}
	let route = url.pathname;
	if (locale !== 'en') {
		const localePrefix = `/${locale}/`;
		if (!route.startsWith(localePrefix)) throw new Error(`Help entry URL has the wrong locale: ${entry.kind} ${entry.code}`);
		route = `/${route.slice(localePrefix.length)}`;
	}
	const segment = (value) => {
		const decoded = value.split('/').map((part) => decodeURIComponent(part));
		if (decoded.some((part) => !part || part === '.' || part === '..')) {
			throw new Error(`Invalid help entry route: ${entry.kind} ${entry.code}`);
		}
		return decoded.join('/');
	};
	let relativePath;
	if (entry.kind === 'guide') {
		if (route === '/learn/') relativePath = 'learn/index.md';
		else if (route.startsWith('/learn/')) relativePath = `learn/${segment(route.slice('/learn/'.length, -1))}.md`;
		else if (route.startsWith('/guide/')) relativePath = `guide/${segment(route.slice('/guide/'.length, -1))}.md`;
	} else {
		const prefix = {
			command: '/reference/commands/',
			term: '/reference/terms/',
			ui: '/reference/controls/',
		}[entry.kind];
		if (prefix && route.startsWith(prefix)) {
			relativePath = `reference/${entry.kind === 'ui' ? 'controls' : `${entry.kind}s`}/${segment(route.slice(prefix.length, -1))}.md`;
		}
	}
	if (!relativePath) throw new Error(`Help entry URL does not match its kind: ${entry.kind} ${entry.code}`);
	return { relativePath, href: url.pathname };
}

function publicWalkthroughAsset(id, asset) {
	return `/walkthroughs/${id}/${asset.replaceAll('\\', '/')}`;
}

function walkPath(root, id, asset) {
	const walkRoot = path.resolve(root, id);
	const target = path.resolve(walkRoot, asset);
	if (!target.startsWith(`${walkRoot}${path.sep}`)) throw new Error(`Walkthrough asset escapes its directory: ${asset}`);
	return target;
}

function resolveHelpTarget(kind, code, entries, walkthroughs) {
	if (kind === 'shot') {
		const [id, stepId, ...rest] = code.split('/');
		if (!id || !stepId || rest.length) return null;
		const manifest = walkthroughs.get(id);
		const step = manifest?.steps.find((candidate) => candidate.id === stepId);
		if (!step) return null;
		return publicWalkthroughAsset(id, step.screenshot);
	}

	// CommonMark forbids spaces in a link target, so multi-word codes arrive percent-encoded, as HelpCatalog expects.
	const entryKind = kind === 'cmd' ? 'command' : kind;
	code = decodeURIComponent(code);
	const entry = entries.find((candidate) => candidate.kind === entryKind && candidate.code === code);
	if (!entry) throw new Error(`Unknown ${kind} help link: ${code}`);
	return entryUrlPath(entry);
}

function rewriteHelpLinks(markdown, entries, walkthroughs) {
	const withScreenshots = markdown.replace(/!\[([^\]]*)\]\(shot:([^)]+)\)/g, (_, alt, code) => {
		const target = resolveHelpTarget('shot', code, entries, walkthroughs);
		return target ? `![${alt}](${target})` : alt;
	});
	return withScreenshots.replace(/(!?\[[^\]]*\]\()((?:cmd|term|ui|guide):[^)]+)(\))/g, (_, prefix, target, suffix) => {
		const colon = target.indexOf(':');
		const destination = resolveHelpTarget(target.slice(0, colon), target.slice(colon + 1), entries, walkthroughs);
		return `${prefix}${destination}${suffix}`;
	});
}

function normalizedLinkTarget(target, sourcePath, docsRoot, includedDocs, repository) {
	if (/^(?:[a-z]+:|\/|#)/i.test(target)) return target;
	const match = target.match(/^([^#?]*)([?#].*)?$/);
	const filePart = match?.[1] ?? target;
	const suffix = match?.[2] ?? '';
	if (!filePart) return target;
	const resolved = path.resolve(path.dirname(sourcePath), decodeURIComponent(filePart));
	if (includedDocs.has(resolved)) {
		const relative = path.relative(docsRoot, resolved).replaceAll('\\', '/').replace(/\.md$/i, '');
		return `/developers/${relative}/${suffix}`;
	}
	const repositoryRelative = path.relative(repository, resolved).replaceAll('\\', '/');
	return `https://github.com/sillsdev/motif/blob/main/${repositoryRelative}${suffix}`;
}

function rewriteDeveloperLinks(markdown, sourcePath, docsRoot, includedDocs, repository) {
	return markdown.replace(/(!?\[[^\]]*\]\()([^)]+)(\))/g, (_, prefix, target, suffix) => {
		return `${prefix}${normalizedLinkTarget(target, sourcePath, docsRoot, includedDocs, repository)}${suffix}`;
	});
}

async function writeHelpPages({ contentRoot, locale, entries, siteRoot, walkthroughs }) {
	for (const entry of entries.filter((candidate) => candidate.kind !== 'guide')) {
		if (entry.helpPage !== null && typeof entry.helpPage !== 'string') {
			throw new Error(`Help entry has invalid Help page content: ${entry.kind} ${entry.code}`);
		}
		const route = entrySiteRoute(entry, siteRoot, locale);
		const markdown = stripFrontmatterAndComments(entry.helpPage ?? '');
		const body = stripDuplicateTitle(rewriteHelpLinks(markdown, entries, walkthroughs), entry.title);
		const content = `${frontmatter(entry.title, entry.description)}${entry.kind === 'command' ? commandMetadata(entry) : ''}${body || entry.description}\n`;
		const localeRoot = locale === 'en' ? contentRoot : path.join(contentRoot, locale);
		const destination = path.join(localeRoot, ...route.relativePath.split('/'));
		await mkdir(path.dirname(destination), { recursive: true });
		await writeFile(destination, content);
	}
}

async function writeGuidePages({ contentRoot, locale, entries, siteRoot, walkthroughs }) {
	const guideEntries = entries.filter((entry) => entry.kind === 'guide');
	const guideByCode = new Map(guideEntries.map((entry) => [entry.code, entry]));
	const outlineCodes = new Set();
	if (guideEntries.length) {
		for (const code of guideCodes) {
			if (outlineCodes.has(code)) throw new Error(`Duplicate Guide outline code: ${code}`);
			outlineCodes.add(code);
			if (!guideByCode.has(code)) throw new Error(`Guide outline references missing Guide entry: ${code}`);
		}
		const unlisted = guideEntries.find((entry) => !entry.code.startsWith('learn/') && !outlineCodes.has(entry.code));
		if (unlisted) throw new Error(`Guide page is missing from the published outline: ${unlisted.code}`);
	}
	for (const [index, entry] of guideEntries.filter((candidate) => !candidate.code.startsWith('learn/')).entries()) {
		if (typeof entry.helpPage !== 'string') throw new Error(`Guide entry has no Help page: ${entry.code}`);
		const route = entrySiteRoute(entry, siteRoot, locale);
		if (!route.relativePath.startsWith('guide/')) throw new Error(`Guide entry URL is not a Guide route: ${entry.code}`);
		const markdown = stripFrontmatterAndComments(entry.helpPage);
		const body = stripDuplicateTitle(rewriteHelpLinks(markdown, entries, walkthroughs), entry.title);
		const localeRoot = locale === 'en' ? contentRoot : path.join(contentRoot, locale);
		const destination = path.join(localeRoot, ...route.relativePath.split('/'));
		await mkdir(path.dirname(destination), { recursive: true });
		await writeFile(destination, `${frontmatter(entry.title, entry.description, guideOrder.get(entry.code) ?? index + 1)}${body}\n`);
	}

	const orderedLearnPages = guideEntries.filter((entry) => entry.code.startsWith('learn/') && entry.code !== 'learn/index')
		.sort((left, right) => {
			const leftOrder = learnOrderByCode.get(left.code) ?? Number.MAX_SAFE_INTEGER;
			const rightOrder = learnOrderByCode.get(right.code) ?? Number.MAX_SAFE_INTEGER;
			return leftOrder - rightOrder || left.code.localeCompare(right.code);
		});
	const learnIndexEntry = guideByCode.get('learn/index');
	if (orderedLearnPages.length && !learnIndexEntry) throw new Error('Guide catalog is missing learn/index.');
	const orderedPages = learnIndexEntry ? [learnIndexEntry, ...orderedLearnPages] : orderedLearnPages;
	const lessonsBySample = new Map();
	const speedLessons = new Map();
	for (const [index, entry] of orderedPages.entries()) {
		if (typeof entry.helpPage !== 'string') throw new Error(`Guide entry has no Help page: ${entry.code}`);
		const route = entrySiteRoute(entry, siteRoot, locale);
		if (!route.relativePath.startsWith('learn/')) throw new Error(`Learn entry URL is not a Learn route: ${entry.code}`);
		const markdown = stripFrontmatterAndComments(entry.helpPage);
		const body = stripDuplicateTitle(rewriteHelpLinks(markdown, entries, walkthroughs), entry.title);
		const localeRoot = locale === 'en' ? contentRoot : path.join(contentRoot, locale);
		const destination = path.join(localeRoot, ...route.relativePath.split('/'));
		await mkdir(path.dirname(destination), { recursive: true });
		const order = entry.code === 'learn/index' ? 0 : index;
		await writeFile(destination, `${frontmatter(entry.title, entry.description, order)}${body}\n`);

		if (entry.code === 'learn/index') continue;
		const slug = entry.code.slice('learn/'.length);
		const lessonPrefix = slug.split('/')[0];
		const languagePrefix = lessonPrefix.split('-')[0];
		const sampleId = lessonPrefix.startsWith('synthetic-')
			? lessonPrefix.split('-').slice(0, 2).join('-')
			: sampleIdByLearnPrefix.get(languagePrefix) ?? `sample-${languagePrefix}`;
		if (!lessonsBySample.has(sampleId)) lessonsBySample.set(sampleId, route.href);
		if (/(^|[-/])speed([-/.]|$)/.test(slug)) speedLessons.set(sampleId, route.href);
	}

	const sections = guideSections.map((section) => {
		const links = section.pages.map((code) => {
			const entry = guideByCode.get(code);
			if (!entry) return null;
			return `- [${entry.title}](${entrySiteRoute(entry, siteRoot, locale).href})`;
		}).filter(Boolean);
		return links.length ? `## ${section.title}\n\n${links.join('\n')}` : '';
	}).filter(Boolean);
	if (guideEntries.length) {
		const localeRoot = locale === 'en' ? contentRoot : path.join(contentRoot, locale);
		const indexPage = path.join(localeRoot, 'guide', 'index.md');
		await mkdir(path.dirname(indexPage), { recursive: true });
		await writeFile(indexPage, `${frontmatter('Guide', 'Learn to use Motif and follow its pages in the order designed for linguists.', 0)}${sections.join('\n\n')}\n`);
	}

	const features = homeFeatureCodes.map((code) => {
		const entry = guideByCode.get(code);
		if (!entry && guideEntries.length) throw new Error(`Homepage feature references missing Guide entry: ${code}`);
		return entry ? {
			title: entry.title,
			description: entry.description,
			href: entrySiteRoute(entry, siteRoot, locale).href,
		} : null;
	}).filter(Boolean);
	return { learnLessons: orderedLearnPages.length, lessonsBySample, speedLessons, features };
}

function validSample(sample, directoryId) {
	return sample.id === directoryId
		&& /^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(sample.id)
		&& typeof sample.title === 'string' && sample.title.trim()
		&& typeof sample.language?.name === 'string' && sample.language.name.trim()
		&& typeof sample.language?.tag === 'string' && sample.language.tag.trim()
		&& Array.isArray(sample.teaches) && sample.teaches.length > 0
		&& sample.teaches.every((item) => typeof item === 'string' && item.trim())
		&& typeof sample.summary === 'string' && sample.summary.trim()
		&& typeof sample.disclaimer === 'string' && sample.disclaimer.trim();
}

function sampleText(value) {
	return value.replace(/[&<>]/g, (character) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[character]).replace(/\s+/g, ' ').trim();
}

async function writeSamplesPage({ samplesRoot, samplesOut, contentRoot, publicRoot, site, locale, lessonsBySample, speedLessons }) {
	const sampleDirectories = await readdir(samplesRoot, { withFileTypes: true }).catch((error) => error.code === 'ENOENT' ? [] : Promise.reject(error));
	const sampleIds = sampleDirectories.filter((entry) => entry.isDirectory()).map((entry) => entry.name).sort();
	const sections = [];
	const homeSamples = [];
	for (const directoryId of sampleIds) {
		const metadataPath = path.join(samplesRoot, directoryId, 'sample.json');
		const sample = JSON.parse(await readFile(metadataPath, 'utf8'));
		if (!validSample(sample, directoryId)) throw new Error(`Invalid sample metadata: ${metadataPath}`);

		const downloads = [];
		const downloadUrls = { fixed: null, broken: null };
		for (const variant of ['fixed', 'broken']) {
			const filename = `${sample.id}-${variant}.fwbackup`;
			const source = path.join(samplesOut, filename);
			const destination = path.join(publicRoot, 'downloads', 'samples', filename);
			try {
				await access(source);
				await mkdir(path.dirname(destination), { recursive: true });
				await copyFile(source, destination);
				downloads.push(`- [${variant === 'fixed' ? 'Fixed' : 'Broken'} project (.fwbackup)](/downloads/samples/${filename})`);
				downloadUrls[variant] = `/downloads/samples/${filename}`;
			} catch (error) {
				if (error.code !== 'ENOENT') throw error;
				downloads.push(`- ${variant === 'fixed' ? 'Fixed' : 'Broken'} project (.fwbackup) is available in release builds.`);
			}
		}

		const speedLessonHref = speedLessons.get(sample.id);
		const hasSpeedLesson = speedLessonHref !== undefined;
		homeSamples.push({
			id: sample.id,
			title: sample.title,
			language: sample.language,
			teaches: sample.teaches,
			summary: sample.summary,
			disclaimer: sample.disclaimer,
			downloads: downloadUrls,
			lessonsHref: lessonsBySample.get(sample.id) ?? null,
			speedLesson: hasSpeedLesson,
			speedLessonHref: speedLessonHref ?? null,
		});
		sections.push([
			`<a id="${sample.id}"></a>`,
			`## ${sampleText(sample.title)}`,
			`**Language:** ${sampleText(sample.language.name)} (${sampleText(sample.language.tag)})`,
			sampleText(sample.summary),
			`**Teaches:** ${sample.teaches.map(sampleText).join(', ')}`,
			`> ${sampleText(sample.disclaimer)}`,
			...(hasSpeedLesson ? ['**Includes a speed lesson**'] : []),
			'### Downloads',
			...downloads,
		].join('\n\n'));
	}

	const localeRoot = locale === 'en' ? contentRoot : path.join(contentRoot, locale);
	const destination = path.join(localeRoot, 'samples', 'index.md');
	await mkdir(path.dirname(destination), { recursive: true });
	await writeFile(destination, `${frontmatter('Samples', 'Practice with small teaching grammars for learning Motif.')}${sections.join('\n\n')}\n`);
	const dataRoot = path.join(site, 'src', 'data');
	await mkdir(dataRoot, { recursive: true });
	await writeFile(path.join(dataRoot, 'samples.json'), `${JSON.stringify(homeSamples, null, 2)}\n`);
	const performancePath = path.join(dataRoot, 'synthetic-turkic-performance.json');
	await rm(performancePath, { force: true });
	try {
		const expectedPath = path.join(samplesRoot, 'synthetic-turkic', 'expected.json');
		const expected = JSON.parse(await readFile(expectedPath, 'utf8'));
		const performance = Object.fromEntries(['fixed', 'broken'].map((variant) => [variant, {
			words: expected[variant]?.words,
			parsed: expected[variant]?.parsed,
			textCoverage: expected[variant]?.textCoverage,
		}]));
		for (const variant of ['fixed', 'broken']) {
			const values = performance[variant];
			if (!Number.isInteger(values?.words) || !Number.isInteger(values?.parsed) || !Number.isFinite(values?.textCoverage)
				|| values.words < 0 || values.parsed < 0 || values.parsed > values.words
				|| values.textCoverage < 0 || values.textCoverage > 1) {
				throw new Error(`Invalid sample performance data: ${expectedPath}`);
			}
		}
		await writeFile(performancePath, `${JSON.stringify(performance, null, 2)}\n`);
	} catch (error) {
		if (error.code !== 'ENOENT') throw error;
	}
	return sampleIds.length;
}

async function listWalkthroughDirectories(root) {
	try {
		const directManifest = path.join(root, 'manifest.json');
		await readFile(directManifest, 'utf8');
		return [{ directory: root, id: path.basename(root) }];
	} catch (error) {
		if (error.code !== 'ENOENT') throw error;
	}
	const entries = await readdir(root, { withFileTypes: true }).catch((error) => error.code === 'ENOENT' ? [] : Promise.reject(error));
	return entries.filter((entry) => entry.isDirectory()).map((entry) => ({ directory: path.join(root, entry.name), id: entry.name })).sort((a, b) => a.id.localeCompare(b.id));
}

function validateWalkthrough(manifest, directoryId) {
	if (manifest.id !== directoryId || !/^[a-z0-9]+(?:-[a-z0-9]+)*$/.test(manifest.id)) {
		throw new Error(`Invalid Walkthrough id: ${manifest.id}`);
	}
	if (!manifest.locale || !manifest.title || !manifest.description || !Array.isArray(manifest.steps) || !manifest.steps.length) {
		throw new Error(`Incomplete Walkthrough manifest: ${manifest.id}`);
	}
	const stepIds = new Set();
	for (const step of manifest.steps) {
		if (!step.id || stepIds.has(step.id) || !step.caption || !step.screenshot || !step.annotated || !Array.isArray(step.callouts)) {
			throw new Error(`Invalid Walkthrough step in ${manifest.id}`);
		}
		stepIds.add(step.id);
	}
	if (manifest.clip !== null && (!manifest.clip || !manifest.clip.webm || !manifest.clip.mp4 || !manifest.clip.webp || !manifest.clip.poster)) {
		throw new Error(`Invalid Walkthrough clip in ${manifest.id}`);
	}
}

async function copyWalkthroughAssets({ walkthroughRoot, item, manifest, publicRoot }) {
	const destinationRoot = path.join(publicRoot, 'walkthroughs', manifest.id);
	await mkdir(destinationRoot, { recursive: true });
	const assets = new Set();
	for (const step of manifest.steps) {
		assets.add(step.screenshot);
		assets.add(step.annotated);
	}
	if (manifest.clip) {
		Object.values(manifest.clip).forEach((asset) => assets.add(asset));
	}
	assets.add(`captions.${manifest.locale}.vtt`);
	for (const asset of assets) {
		const source = item.directory === walkthroughRoot && item.id === manifest.id
			? path.join(item.directory, asset)
			: walkPath(walkthroughRoot, manifest.id, asset);
		const destination = path.join(destinationRoot, asset);
		await mkdir(path.dirname(destination), { recursive: true });
		await copyFile(source, destination);
	}
}

async function writeWalkthroughPages({ walkthroughRoot, contentRoot, dataRoot, publicRoot }) {
	const walkthroughs = new Map();
	const items = await listWalkthroughDirectories(walkthroughRoot);
	for (const item of items) {
		const manifestPath = path.join(item.directory, 'manifest.json');
		const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
		validateWalkthrough(manifest, item.id);
		walkthroughs.set(manifest.id, manifest);
		await copyWalkthroughAssets({ walkthroughRoot, item, manifest, publicRoot });
		const dataDirectory = path.join(dataRoot, manifest.id);
		await mkdir(dataDirectory, { recursive: true });
		await writeFile(path.join(dataDirectory, 'manifest.json'), JSON.stringify(manifest, null, 2));
		const pageLocaleRoot = manifest.locale === 'en' ? contentRoot : path.join(contentRoot, manifest.locale);
		const destination = path.join(pageLocaleRoot, 'guide', 'walkthroughs', `${manifest.id}.mdx`);
		await mkdir(path.dirname(destination), { recursive: true });
		const componentPath = '../../../../components/Walkthrough.astro';
		const dataPath = `../../../../data/walkthroughs/${manifest.id}/manifest.json`;
		const page = `${frontmatter(manifest.title, manifest.description, 1000)}import Walkthrough from '${componentPath}';\nimport manifest from '${dataPath}';\n\n<Walkthrough manifest={manifest} assetBase="/walkthroughs/${manifest.id}" />\n`;
		await writeFile(destination, page);
	}
	return walkthroughs;
}

async function writeDeveloperDocs({ repository, docsRoot, contentRoot }) {
	// ADRs stay on GitHub: they record how decisions were reached, which is not what a site reader came for.
	const rootEntries = await readdir(docsRoot, { withFileTypes: true });
	const allFiles = rootEntries
		.filter((entry) => entry.isFile() && entry.name.endsWith('.md'))
		.map((entry) => path.join(docsRoot, entry.name))
		.sort();
	const includedDocs = new Set(allFiles.map((file) => path.resolve(file)));
	for (const file of allFiles) {
		const relative = path.relative(docsRoot, file);
		const destination = path.join(contentRoot, 'developers', relative);
		const original = await readFile(file, 'utf8');
		const title = markdownTitle(original, path.basename(file, '.md'));
		const description = markdownDescription(original, `Developer documentation for ${title}.`);
		const stripped = stripFrontmatterAndComments(original);
		const body = stripDuplicateTitle(rewriteDeveloperLinks(stripped, file, docsRoot, includedDocs, repository), title);
		await mkdir(path.dirname(destination), { recursive: true });
		await writeFile(destination, `${frontmatter(title, description)}${body}
`);
	}
}

async function cleanGeneratedPaths(paths) {
	for (const target of paths) {
		await rm(target, { recursive: true, force: true });
		await mkdir(target, { recursive: true });
	}
}

export async function syncSiteContent({ repository, site, helpExportPath, walkthroughRoot, docsRoot, apiXmlPath, samplesRoot, samplesOut }) {
	const contentRoot = path.join(site, 'src', 'content', 'docs');
	const publicRoot = path.join(site, 'public');
	const helpExport = JSON.parse(await readFile(helpExportPath, 'utf8'));
	if (!helpExport.locale || !helpExport.siteRoot || !Array.isArray(helpExport.entries)) throw new Error('Invalid help export.');
	const localeRoot = helpExport.locale === 'en' ? contentRoot : path.join(contentRoot, helpExport.locale);
	const generatedPaths = [
		path.join(contentRoot, 'reference', 'commands'),
		path.join(contentRoot, 'reference', 'terms'),
		path.join(contentRoot, 'reference', 'controls'),
		path.join(contentRoot, 'reference', 'api'),
		path.join(contentRoot, 'guide'),
		path.join(localeRoot, 'learn'),
		path.join(localeRoot, 'samples'),
		path.join(contentRoot, 'developers'),
		path.join(site, 'src', 'data', 'walkthroughs'),
		path.join(publicRoot, 'walkthroughs'),
		path.join(publicRoot, 'downloads', 'samples'),
	];
	await cleanGeneratedPaths(generatedPaths);

	const seenEntries = new Set();
	for (const entry of helpExport.entries) {
		const key = `${entry.kind}:${entry.code}`;
		const expectedSlug = entry.kind === 'guide' ? entry.code : slugify(entry.code);
		const validGuideCode = entry.kind !== 'guide' || /^[a-z0-9]+(?:-[a-z0-9]+)*(?:\/[a-z0-9]+(?:-[a-z0-9]+)*)*$/.test(entry.code);
		if (!entry.kind || !entry.code || !entry.title || !entry.description || !validGuideCode || entry.slug !== expectedSlug || !entry.url) {
			throw new Error(`Invalid help entry: ${entry.code ?? '(missing code)'}`);
		}
		if (seenEntries.has(key)) throw new Error(`Duplicate help entry: ${entry.kind} ${entry.code}`);
		seenEntries.add(key);
		entrySiteRoute(entry, helpExport.siteRoot, helpExport.locale);
	}
	const walkthroughs = await writeWalkthroughPages({
		walkthroughRoot,
		contentRoot,
		dataRoot: path.join(site, 'src', 'data', 'walkthroughs'),
		publicRoot,
	});
	await writeHelpPages({ contentRoot, locale: helpExport.locale, entries: helpExport.entries, siteRoot: helpExport.siteRoot, walkthroughs });
	const learn = await writeGuidePages({ contentRoot, locale: helpExport.locale, entries: helpExport.entries, siteRoot: helpExport.siteRoot, walkthroughs });
	const dataRoot = path.join(site, 'src', 'data');
	await mkdir(dataRoot, { recursive: true });
	await writeFile(path.join(dataRoot, 'guide-features.json'), `${JSON.stringify(learn.features, null, 2)}\n`);
	const samples = await writeSamplesPage({
		samplesRoot,
		samplesOut,
		contentRoot,
		publicRoot,
		site,
		locale: helpExport.locale,
		lessonsBySample: learn.lessonsBySample,
		speedLessons: learn.speedLessons,
	});
	await writeDeveloperDocs({ repository, docsRoot, contentRoot });

	const xml = await readFile(apiXmlPath, 'utf8');
	const apiMarkdown = convertXmlDocsToMarkdown(xml);
	const apiPage = path.join(contentRoot, 'reference', 'api', 'index.md');
	await mkdir(path.dirname(apiPage), { recursive: true });
	await writeFile(apiPage, `${frontmatter('API Reference', 'Public contract types generated from XML documentation comments.')}${apiMarkdown}`);
	return { entries: helpExport.entries.length, walkthroughs: walkthroughs.size, learnLessons: learn.learnLessons, samples };
}
