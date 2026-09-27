import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const execFileAsync = promisify(execFile);
const siteRoot = fileURLToPath(new URL('..', import.meta.url));
const outputRoot = path.join(siteRoot, 'dist');

async function htmlPages(directory = outputRoot) {
	const pages = [];
	for (const entry of await readdir(directory, { withFileTypes: true })) {
		const entryPath = path.join(directory, entry.name);
		if (entry.isDirectory()) pages.push(...await htmlPages(entryPath));
		else if (entry.name.endsWith('.html')) pages.push(entryPath);
	}
	return pages;
}

test('built pages render the beta banner above every Starlight header', async () => {
	await execFileAsync(process.platform === 'win32' ? 'npm.cmd' : 'npm', ['run', 'build'], {
		cwd: siteRoot,
		shell: process.platform === 'win32',
		maxBuffer: 20 * 1024 * 1024,
	});

	const pages = await htmlPages();
	assert.ok(pages.length > 1, 'the site build should emit multiple HTML pages');
	for (const page of pages) {
		const html = await readFile(page, 'utf8');
		const banner = html.indexOf('data-beta-banner');
		const navigation = html.indexOf('class="home-brand');
		assert.notEqual(banner, -1, `${path.relative(outputRoot, page)} has the beta banner`);
		assert.ok(navigation > banner, `${path.relative(outputRoot, page)} puts the banner above its navigation`);
		assert.match(html, /Motif is a pilot project within SIL Language Technology/);
	}
});

test('the home page renders the approved beta hero, audience situations, FAQ, and footer copy', async () => {
	const html = await readFile(path.join(outputRoot, 'index.html'), 'utf8');

	assert.match(html, /Beta · For anyone getting a FieldWorks parser to work/);
	assert.match(html, />Download the beta</);
	assert.match(html, /Beta for Windows · Free and open source · Not yet an officially supported SIL product/);

	const audienceMarkup = html.slice(html.indexOf('id="who-its-for"'), html.indexOf('id="clips"'));
	const audience = audienceMarkup.replace(/<[^>]+>/g, '').replace(/&#39;|&#x27;|&apos;/gi, "'");
	for (const situation of [
		"You've described the morphology, but the parser still disagrees.",
		'You just want your text glossed.',
		"You know when a word is right. The computer doesn't — yet.",
		'You need to know which rule is slow, and why.',
	]) assert.ok(audience.includes(situation), `the audience cards include: ${situation}`);
	assert.doesNotMatch(audienceMarkup, /<blockquote\b/);

	const faq = html.slice(html.indexOf('id="before-you-start"'), html.indexOf('class="motif-site-footer"'));
	for (const text of [
		'Before you start',
		'Is Motif ready for my project?',
		'Is Motif an official SIL product?',
		'Something went wrong. What now?',
		'keep a FieldWorks backup, and try it on a copy before your real project.',
		'free, open source and actively developed, but not yet officially supported.',
		'include what you did, what you expected, and what happened.',
	]) assert.ok(faq.includes(text), `the FAQ includes: ${text}`);

	assert.match(html.replace(/<[^>]+>/g, ''), /Motif \(beta\) is a pilot project within SIL Language Technology/);
	assert.match(html, /Report a problem/);
	assert.match(html, />License</);

	const sampleCards = html.slice(html.indexOf('class="home-sample-grid"'), html.indexOf('Browse all sample languages'));
	for (const [title, language] of [
		['Synthetic Turkic-style sample', 'Turkish'],
		['Synthetic Bantu-style sample', 'Swahili'],
		['Synthetic Philippine-style sample', 'Tagalog'],
	]) {
		assert.match(sampleCards, /SYNTHETIC EXAMPLE/);
		assert.ok(sampleCards.includes(title), `the sample cards include ${title}`);
		assert.ok(sampleCards.includes(`Generated to demonstrate Motif. Modelled loosely on ${language}; not real ${language} data and not a description of any language.`));
	}
	const syncedSamples = JSON.parse(await readFile(path.join(siteRoot, 'src', 'data', 'samples.json'), 'utf8'));
	assert.equal(syncedSamples.find((sample) => sample.id === 'synthetic-turkic').lessonsHref, '/learn/turkish-plural-harmony/');
});

test('synthetic sample fixtures identify generated data plainly', async () => {
	for (const [id, title, language] of [
		['synthetic-turkic', 'Synthetic Turkic-style sample', 'Turkish'],
		['synthetic-bantu', 'Synthetic Bantu-style sample', 'Swahili'],
		['synthetic-philippine', 'Synthetic Philippine-style sample', 'Tagalog'],
	]) {
		const fixturePath = path.join(siteRoot, 'fixtures', 'samples', id, 'sample.json');
		const sample = JSON.parse(await readFile(fixturePath, 'utf8'));
		assert.equal(sample.id, id);
		assert.equal(sample.title, title);
		assert.equal(sample.disclaimer, `Generated to demonstrate Motif. Modelled loosely on ${language}; not real ${language} data and not a description of any language.`);
	}
});

test('banner dismissal persists when storage works and still renders when it does not', async () => {
	const { initializeBetaBanners } = await import('../src/scripts/beta-banner.mjs');
	const createBanner = () => {
		const listeners = new Map();
		const button = { addEventListener: (event, listener) => listeners.set(event, listener) };
		return {
			banner: { hidden: false, querySelector: () => button },
			click: () => listeners.get('click')(),
		};
	};
	const documentFor = (banner) => ({
		querySelectorAll: () => [banner],
		documentElement: { classList: { add: () => {} } },
	});

	const banner = createBanner();
	const values = new Map();
	const storage = {
		getItem: (key) => values.get(key) ?? null,
		setItem: (key, value) => values.set(key, value),
	};
	initializeBetaBanners(documentFor(banner.banner), storage);
	banner.click();
	assert.equal(banner.banner.hidden, true);
	assert.equal(values.get('motif-beta-banner-dismissed'), 'true');

	const unavailable = createBanner();
	initializeBetaBanners(documentFor(unavailable.banner), {
		getItem: () => { throw new Error('storage is unavailable'); },
		setItem: () => { throw new Error('storage is unavailable'); },
	});
	assert.equal(unavailable.banner.hidden, false);
	assert.doesNotThrow(() => unavailable.click());
	assert.equal(unavailable.banner.hidden, true);
});
