import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

const requiredPathArguments = [
	'--help-export',
	'--help-root',
	'--walkthrough-output',
	'--api-xml',
	'--samples-root',
	'--samples-out',
	'--docs-root',
	'--configuration',
];

export function validateProductionSyncArguments(argv, { repository, site }) {
	const production = argv.includes('--production');
	if (argv.includes('--require-videos') && !production) {
		throw new Error('--require-videos requires --production.');
	}
	if (!production) return null;

	const missing = requiredPathArguments.filter((name) => argumentValue(argv, name) === undefined);
	if (missing.length) {
		throw new Error('Production sync requires explicit paths: ' + missing.join(', ') + '.');
	}

	const repositoryRoot = path.resolve(repository);
	const configuration = argumentValue(argv, '--configuration');
	if (!['Debug', 'Release'].includes(configuration)) {
		throw new Error('Production sync configuration must be Debug or Release: ' + configuration);
	}
	const inputs = Object.fromEntries(requiredPathArguments
		.filter((name) => name !== '--configuration')
		.map((name) => [camelName(name.slice(2)), path.resolve(argumentValue(argv, name))]));
	for (const [name, value] of Object.entries(inputs)) {
		if (containsFixtures(value)) {
			throw new Error('Production sync refuses fixture input for --' + kebabName(name) + ': ' + value);
		}
	}

	const buildOutput = path.join(repositoryRoot, 'bin', configuration);
	const validationOutput = path.join(buildOutput, 'documentation-validation');
	if (!isWithin(inputs.helpExport, validationOutput) ||
		!isWithin(inputs.walkthroughOutput, validationOutput) ||
		!samePath(path.dirname(inputs.helpExport), path.dirname(inputs.walkthroughOutput))) {
		throw new Error('Production Help export and walkthrough output must come from the same documentation validation run.');
	}
	const expectedApiXml = path.join(buildOutput, 'SIL.Motif.Contract.xml');
	if (!samePath(inputs.apiXml, expectedApiXml)) {
		throw new Error('--api-xml must be SIL.Motif.Contract.xml from the selected ' +
			configuration + ' build output: ' + inputs.apiXml);
	}
	const expectedSamplesRoot = path.join(repositoryRoot, 'samples');
	if (!samePath(inputs.samplesRoot, expectedSamplesRoot)) {
		throw new Error('--samples-root must be the repository sample source: ' + inputs.samplesRoot);
	}
	const expectedSamplesOut = path.join(buildOutput, 'samples');
	if (!samePath(inputs.samplesOut, expectedSamplesOut)) {
		throw new Error('--samples-out must come from the selected ' + configuration +
			' build output: ' + inputs.samplesOut);
	}
	const expectedDocsRoot = path.join(repositoryRoot, 'docs');
	if (!samePath(inputs.docsRoot, expectedDocsRoot)) {
		throw new Error('--docs-root must be the repository docs directory: ' + inputs.docsRoot);
	}

	return {
		...inputs,
		configuration,
		production: true,
		requireVideos: argv.includes('--require-videos'),
	};
}

export async function validateProductionHelpExport(helpExportPath, helpRoot) {
	let helpExport;
	try {
		helpExport = JSON.parse(await readFile(helpExportPath, 'utf8'));
	} catch (error) {
		throw new Error('Could not read the production Help export at ' + helpExportPath + ': ' + error.message);
	}
	if (typeof helpExport.locale !== 'string' || !/^[A-Za-z0-9-]+$/.test(helpExport.locale) ||
		!Array.isArray(helpExport.entries) || helpExport.entries.length === 0) {
		throw new Error('Production Help export must contain a locale and entries.');
	}

	const guideRoot = path.join(helpRoot, helpExport.locale, 'guide');
	const expected = await markdownSlugs(guideRoot);
	const guideEntries = helpExport.entries.filter((entry) => entry.kind === 'guide');
	if (guideEntries.some((entry) => typeof entry.code !== 'string')) {
		throw new Error('Production Help export contains a Guide entry without a code.');
	}
	const actual = guideEntries.map((entry) => entry.code).sort();
	if (!expected.length) throw new Error('No authored Guide pages were found in ' + guideRoot + '.');
	if (actual.length !== expected.length || actual.some((code, index) => code !== expected[index])) {
		throw new Error('Production Help export Guide inventory does not match authored pages in ' + guideRoot + '.');
	}
	return helpExport;
}

async function markdownSlugs(root, relativeRoot = root) {
	const files = [];
	for (const entry of await readdir(root, { withFileTypes: true })) {
		const fullPath = path.join(root, entry.name);
		if (entry.isDirectory()) files.push(...await markdownSlugs(fullPath, relativeRoot));
		else if (entry.isFile() && entry.name.toLowerCase().endsWith('.md')) {
			files.push(path.relative(relativeRoot, fullPath).replaceAll('\\', '/').replace(/\.md$/i, ''));
		}
	}
	return files.sort();
}

function argumentValue(argv, name) {
	const inline = argv.filter((value) => value.startsWith(name + '='));
	const positions = argv.flatMap((value, index) => value === name ? [index] : []);
	if (inline.length + positions.length > 1) throw new Error('Production sync received ' + name + ' more than once.');
	if (inline.length) return requireValue(inline[0].slice(name.length + 1), name);
	if (!positions.length) return undefined;
	return requireValue(argv[positions[0] + 1], name);
}

function requireValue(value, name) {
	if (typeof value !== 'string' || !value.trim() || value.startsWith('--')) {
		throw new Error('Production sync requires a value for ' + name + '.');
	}
	return value;
}

function camelName(value) {
	return value.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
}

function kebabName(value) {
	return value.replace(/[A-Z]/g, (letter) => '-' + letter.toLowerCase());
}

function containsFixtures(value) {
	return path.resolve(value).split(path.sep).some((part) => part.toLowerCase() === 'fixtures');
}

function samePath(left, right) {
	return path.relative(path.resolve(left), path.resolve(right)) === '' &&
		path.relative(path.resolve(right), path.resolve(left)) === '';
}

function isWithin(candidate, root) {
	const relative = path.relative(path.resolve(root), path.resolve(candidate));
	return relative !== '' && relative !== '..' && !relative.startsWith(`..${path.sep}`) && !path.isAbsolute(relative);
}
