import { access, copyFile, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { convertXmlDocsToMarkdown } from '../../tools/xml-docs-to-markdown.mjs';

const guideSections = [
	{
		title: 'Get started',
		pages: [
			['what-is-motif', 'What Motif does'],
			['install', 'Installing Motif'],
			['open-a-project', 'Opening a project'],
			['first-run-setup', 'Choosing what to measure'],
			['reading-the-overview', 'Reading the Overview'],
		],
	},
	{
		title: 'Pages of the window',
		pages: [
			['overview', 'Overview'],
			['texts', 'Texts: Text Coverage and choosing texts'],
			['try-a-word', 'Try a Word: parse one word and see why'],
			['timing', 'Timing: which words are slow to parse'],
			['warnings', 'Warnings: grammar findings and what to do about them'],
			['review-changes', 'Review changes: what applying would do'],
			['ai-handoff', 'AI Handoff: giving an AI agent the context it needs'],
		],
	},
	{
		title: 'Everyday tasks',
		pages: [
			['refresh-numbers', 'Bring the numbers up to date after FieldWorks saves'],
			['change-an-analysis', 'Change an analysis and keep it pending'],
			['replace-a-pending-change', 'Replace or remove a pending change'],
			['apply-to-fieldworks', 'Apply your changes to the FieldWorks project'],
			['when-a-change-no-longer-fits', 'A change that no longer fits'],
			['switch-projects', 'Switch between projects'],
			['cancel-a-long-run', 'Cancel a run that is taking too long'],
			['when-something-goes-wrong', 'Crash reports, diagnostics, and what to send'],
		],
	},
	{
		title: 'Concepts',
		pages: [
			['baseline', 'Baseline'],
			['assessment', 'Assessment'],
			['text-coverage', 'Text Coverage'],
			['default-selection', 'Default Selection'],
			['drift', 'Drift'],
			['pending-changes', 'Pending changes'],
		],
	},
	{
		title: 'For AI agents and scripts',
		pages: [
			['agents/start-here', 'Using Motif from an agent'],
			['agents/output-and-exit-codes', 'The JSON envelope, failure contract and exit codes'],
			['agents/measure-a-grammar', 'Measure a grammar'],
			['agents/work-with-jobs', 'Work with jobs'],
			['agents/handoff', 'Read a Handoff'],
		],
	},
];
const guideOrder = new Map(guideSections.flatMap((section, sectionIndex) => section.pages.map(([slug], pageIndex) => [slug, sectionIndex * 100 + pageIndex])));

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

function entryKindPath(entry) {
	if (entry.kind === 'command') return `reference/commands/${entry.slug}`;
	if (entry.kind === 'term') return `reference/terms/${entry.slug}`;
	if (entry.kind === 'ui') return `reference/controls/${entry.slug}`;
	throw new Error(`Unknown help entry kind: ${entry.kind}`);
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
	return `/${entryKindPath(entry)}/`;
}

function rewriteHelpLinks(markdown, entries, walkthroughs) {
	const withScreenshots = markdown.replace(/!\[([^\]]*)\]\(shot:([^)]+)\)/g, (_, alt, code) => {
		const target = resolveHelpTarget('shot', code, entries, walkthroughs);
		return target ? `![${alt}](${target})` : alt;
	});
	return withScreenshots.replace(/(!?\[[^\]]*\]\()((?:cmd|term|ui):[^)]+)(\))/g, (_, prefix, target, suffix) => {
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

async function markdownFiles(root) {
	const files = [];
	for (const entry of await readdir(root, { withFileTypes: true })) {
		const fullPath = path.join(root, entry.name);
		if (entry.isDirectory()) files.push(...await markdownFiles(fullPath));
		else if (entry.isFile() && entry.name.endsWith('.md')) files.push(fullPath);
	}
	return files.sort();
}

async function writeHelpPages({ helpRoot, outputRoot, locale, entries, walkthroughs }) {
	const mdFiles = await markdownFiles(helpRoot).catch((error) => error.code === 'ENOENT' ? [] : Promise.reject(error));
	const sourcePages = new Map();
	for (const file of mdFiles) {
		const relative = path.relative(helpRoot, file).replaceAll('\\', '/');
		sourcePages.set(relative, await readFile(file, 'utf8'));
	}

	for (const entry of entries) {
		const route = entryKindPath(entry);
		const sourcePath = `${entry.kind === 'ui' ? 'ui' : `${entry.kind}s`}/${entry.slug}.md`;
		const body = stripFrontmatterAndComments(sourcePages.get(sourcePath) ?? (typeof entry.helpPage === 'string' ? entry.helpPage : ''));
		const title = entry.title || entry.code;
		const description = entry.description || `Help for ${entry.code}.`;
		const safeBody = stripDuplicateTitle(rewriteHelpLinks(body, entries, walkthroughs), title);
		const content = `${frontmatter(title, description)}${entry.kind === 'command' ? commandMetadata(entry) : ''}${safeBody || description}\n`;
		const localeRoot = locale === 'en' ? outputRoot : path.join(outputRoot, locale);
		const destination = path.join(localeRoot, route + '.md');
		await mkdir(path.dirname(destination), { recursive: true });
		await writeFile(destination, content);
	}
}

async function writeGuidePages({ helpRoot, contentRoot, locale, entries, walkthroughs }) {
	const guideRoot = path.join(helpRoot, 'guide');
	const sources = await markdownFiles(guideRoot).catch((error) => error.code === 'ENOENT' ? [] : Promise.reject(error));
	const pages = new Map();
	const learnPages = new Map();
	for (const sourcePath of sources) {
		const slug = path.relative(guideRoot, sourcePath).replaceAll('\\', '/').replace(/\.md$/i, '');
		if (slug.startsWith('learn/')) {
			learnPages.set(slug.slice('learn/'.length), { sourcePath });
		} else {
			if (!guideOrder.has(slug)) throw new Error(`Guide page is missing from the published outline: ${slug}`);
			pages.set(slug, { sourcePath, order: guideOrder.get(slug) + 1 });
		}
	}

	for (const [slug, page] of pages) {
		const markdown = stripFrontmatterAndComments(await readFile(page.sourcePath, 'utf8'));
		const title = markdownTitle(markdown, slug.split('/').at(-1));
		const description = markdownDescription(markdown, `Guide to ${title}.`);
		const body = stripDuplicateTitle(rewriteHelpLinks(markdown, entries, walkthroughs), title);
		const localeRoot = locale === 'en' ? contentRoot : path.join(contentRoot, locale);
		const destination = path.join(localeRoot, 'guide', `${slug}.md`);
		await mkdir(path.dirname(destination), { recursive: true });
		await writeFile(destination, `${frontmatter(title, description, page.order)}${body}\n`);
	}

	const learnLinks = [];
	for (const [slug, page] of learnPages) {
		const markdown = stripFrontmatterAndComments(await readFile(page.sourcePath, 'utf8'));
		const title = markdownTitle(markdown, slug.split('/').at(-1));
		const description = markdownDescription(markdown, `Learn from ${title}.`);
		const body = stripDuplicateTitle(rewriteHelpLinks(markdown, entries, walkthroughs), title);
		const localeRoot = locale === 'en' ? contentRoot : path.join(contentRoot, locale);
		const destination = path.join(localeRoot, 'learn', `${slug}.md`);
		await mkdir(path.dirname(destination), { recursive: true });
		await writeFile(destination, `${frontmatter(title, description)}${body}\n`);
		learnLinks.push(`- [${title}](/learn/${slug}/)`);
	}

	const sections = guideSections.map((section) => {
		const links = section.pages
			.filter(([slug]) => pages.has(slug))
			.map(([slug, title]) => `- [${title}](/guide/${slug}/)`);
		return links.length ? `## ${section.title}\n\n${links.join('\n')}` : '';
	}).filter(Boolean);
	const localeRoot = locale === 'en' ? contentRoot : path.join(contentRoot, locale);
	const indexPage = path.join(localeRoot, 'guide', 'index.md');
	await mkdir(path.dirname(indexPage), { recursive: true });
	await writeFile(indexPage, `${frontmatter('Guide', 'Learn to use Motif and follow its pages in the order designed for linguists.', 0)}${sections.join('\n\n')}\n`);
	const learnIndex = path.join(localeRoot, 'learn', 'index.md');
	await mkdir(path.dirname(learnIndex), { recursive: true });
	await writeFile(learnIndex, `${frontmatter('Learn', 'Step-by-step lessons for learning Motif with sample language projects.')}${learnLinks.join('\n')}\n`);
	const speedSamples = new Set([...learnPages.keys()]
		.filter((slug) => /(^|[-/])speed([-/.]|$)/.test(slug))
		.map((slug) => `sample-${slug.split('/')[0].split('-')[0]}`));
	return { learnLessons: learnPages.size, speedSamples };
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

async function writeSamplesPage({ samplesRoot, samplesOut, contentRoot, publicRoot, site, locale, speedSamples }) {
	const sampleDirectories = await readdir(samplesRoot, { withFileTypes: true }).catch((error) => error.code === 'ENOENT' ? [] : Promise.reject(error));
	const sampleIds = sampleDirectories.filter((entry) => entry.isDirectory()).map((entry) => entry.name).sort();
	const sections = [];
	const homeSamples = [];
	for (const directoryId of sampleIds) {
		const metadataPath = path.join(samplesRoot, directoryId, 'sample.json');
		const sample = JSON.parse(await readFile(metadataPath, 'utf8'));
		if (!validSample(sample, directoryId)) throw new Error(`Invalid sample metadata: ${metadataPath}`);

		const downloads = [];
		for (const variant of ['fixed', 'broken']) {
			const filename = `${sample.id}-${variant}.fwbackup`;
			const source = path.join(samplesOut, filename);
			const destination = path.join(publicRoot, 'downloads', 'samples', filename);
			try {
				await access(source);
				await mkdir(path.dirname(destination), { recursive: true });
				await copyFile(source, destination);
				downloads.push(`- [${variant === 'fixed' ? 'Fixed' : 'Broken'} project (.fwbackup)](/downloads/samples/${filename})`);
			} catch (error) {
				if (error.code !== 'ENOENT') throw error;
				downloads.push(`- ${variant === 'fixed' ? 'Fixed' : 'Broken'} project (.fwbackup) is available in release builds.`);
			}
		}

		const hasSpeedLesson = speedSamples.has(sample.id);
		homeSamples.push({ id: sample.id, title: sample.title, language: sample.language, summary: sample.summary, speedLesson: hasSpeedLesson });
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
	const performancePath = path.join(dataRoot, 'sample-turkish-performance.json');
	await rm(performancePath, { force: true });
	try {
		const expected = JSON.parse(await readFile(path.join(samplesRoot, 'sample-turkish', 'expected.json'), 'utf8'));
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
				throw new Error(`Invalid sample performance data: ${path.join(samplesRoot, 'sample-turkish', 'expected.json')}`);
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
	const rootEntries = await readdir(docsRoot, { withFileTypes: true });
	const adrEntries = await readdir(path.join(docsRoot, 'adr'), { withFileTypes: true });
	const allFiles = [
		...rootEntries.filter((entry) => entry.isFile() && entry.name.endsWith('.md')).map((entry) => path.join(docsRoot, entry.name)),
		...adrEntries.filter((entry) => entry.isFile() && entry.name.endsWith('.md')).map((entry) => path.join(docsRoot, 'adr', entry.name)),
	].sort();
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
		await writeFile(destination, `${frontmatter(title, description)}${body}\n`);
	}

	const decisions = [];
	for (const entry of adrEntries.filter((candidate) => candidate.isFile() && candidate.name.endsWith('.md')).sort((a, b) => a.name.localeCompare(b.name))) {
		const source = path.join(docsRoot, 'adr', entry.name);
		const original = await readFile(source, 'utf8');
		const title = markdownTitle(original, path.basename(entry.name, '.md'));
		const slug = path.basename(entry.name, '.md');
		decisions.push(`- [${title}](/developers/adr/${slug}/)`);
	}
	const indexPath = path.join(contentRoot, 'developers', 'adr', 'index.md');
	await mkdir(path.dirname(indexPath), { recursive: true });
	await writeFile(indexPath, `${frontmatter('Accepted decisions', 'Architectural decisions recorded for Motif.', 0)}${decisions.join('\n')}\n`);
}

async function cleanGeneratedPaths(paths) {
	for (const target of paths) {
		await rm(target, { recursive: true, force: true });
		await mkdir(target, { recursive: true });
	}
}

export async function syncSiteContent({ repository, site, helpExportPath, helpRoot, walkthroughRoot, docsRoot, apiXmlPath, samplesRoot, samplesOut }) {
	const contentRoot = path.join(site, 'src', 'content', 'docs');
	const publicRoot = path.join(site, 'public');
	const helpExport = JSON.parse(await readFile(helpExportPath, 'utf8'));
	if (!helpExport.locale || !Array.isArray(helpExport.entries)) throw new Error('Invalid help export.');
	try {
		await readdir(path.join(helpRoot, helpExport.locale));
		helpRoot = path.join(helpRoot, helpExport.locale);
	} catch (error) {
		if (error.code !== 'ENOENT') throw error;
	}
	const localeRoot = helpExport.locale === 'en' ? contentRoot : path.join(contentRoot, helpExport.locale);
	const generatedPaths = [
		path.join(contentRoot, 'reference', 'commands'),
		path.join(contentRoot, 'reference', 'terms'),
		path.join(contentRoot, 'reference', 'controls'),
		path.join(contentRoot, 'reference', 'api'),
		path.join(contentRoot, 'guide', 'walkthroughs'),
		path.join(localeRoot, 'learn'),
		path.join(localeRoot, 'samples'),
		path.join(contentRoot, 'developers'),
		path.join(site, 'src', 'data', 'walkthroughs'),
		path.join(publicRoot, 'walkthroughs'),
		path.join(publicRoot, 'downloads', 'samples'),
	];
	await cleanGeneratedPaths(generatedPaths);

	for (const entry of helpExport.entries) {
		if (!entry.kind || !entry.code || !entry.title || !entry.description || entry.slug !== slugify(entry.code)) {
			throw new Error(`Invalid help entry: ${entry.code ?? '(missing code)'}`);
		}
	}
	const walkthroughs = await writeWalkthroughPages({
		walkthroughRoot,
		contentRoot,
		dataRoot: path.join(site, 'src', 'data', 'walkthroughs'),
		publicRoot,
	});
	await writeHelpPages({ helpRoot, outputRoot: contentRoot, locale: helpExport.locale, entries: helpExport.entries, walkthroughs });
	const learn = await writeGuidePages({ helpRoot, contentRoot, locale: helpExport.locale, entries: helpExport.entries, walkthroughs });
	const samples = await writeSamplesPage({ samplesRoot, samplesOut, contentRoot, publicRoot, site, locale: helpExport.locale, speedSamples: learn.speedSamples });
	await writeDeveloperDocs({ repository, docsRoot, contentRoot });

	const xml = await readFile(apiXmlPath, 'utf8');
	const apiMarkdown = convertXmlDocsToMarkdown(xml);
	const apiPage = path.join(contentRoot, 'reference', 'api', 'index.md');
	await mkdir(path.dirname(apiPage), { recursive: true });
	await writeFile(apiPage, `${frontmatter('API Reference', 'Public contract types generated from XML documentation comments.')}${apiMarkdown}`);
	return { entries: helpExport.entries.length, walkthroughs: walkthroughs.size, learnLessons: learn.learnLessons, samples };
}
