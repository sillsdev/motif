import { createHash } from 'node:crypto';
import { lstat, readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

export async function resolveSyntheticSampleRoot(repository, configuredRoot) {
	const lock = JSON.parse(await readFile(path.join(repository, 'evals', 'synthetic-samples.lock.json'), 'utf8'));
	if (!Array.isArray(lock.files) || lock.files.length !== 6) throw new Error('Synthetic sample input lock must name exactly six files.');
	const root = path.resolve(configuredRoot ?? path.join(repository, 'bin', '.cache', 'synthetic-samples', lock.commit, 'samples'));
	const rootStat = await lstat(root).catch(() => null);
	if (!rootStat?.isDirectory() || rootStat.isSymbolicLink()) {
		throw new Error(`Verified synthetic sample input root is missing or linked: ${root}. Provision release ${lock.release} with ./evals/Get-SyntheticSamples.ps1 -Download.`);
	}
	const expectedFiles = new Set();
	for (const entry of lock.files) {
		if (!entry.path.startsWith('samples/')) throw new Error(`Invalid synthetic sample lock path: ${entry.path}`);
		const relative = entry.path.slice('samples/'.length);
		if (relative.split('/').some((part) => !part || part === '.' || part === '..')) throw new Error(`Unsafe synthetic sample lock path: ${entry.path}`);
		expectedFiles.add(relative);
	}
	if (expectedFiles.size !== 6) throw new Error('Synthetic sample input lock must name six distinct files.');
	const files = [];
	const directories = [];
	async function visit(directory, relative = '') {
		for (const item of await readdir(directory, { withFileTypes: true })) {
			const itemPath = path.join(directory, item.name);
			const itemRelative = relative ? `${relative}/${item.name}` : item.name;
			const stat = await lstat(itemPath);
			if (stat.isSymbolicLink()) throw new Error(`Linked synthetic sample input is not allowed: ${itemPath}`);
			if (stat.isDirectory()) {
				directories.push(itemRelative);
				await visit(itemPath, itemRelative);
			} else if (stat.isFile()) files.push(itemRelative);
			else throw new Error(`Unsupported entry in synthetic sample inputs: ${itemPath}`);
		}
	}
	await visit(root);
	const expectedDirectories = new Set(['synthetic-bantu', 'synthetic-turkic']);
	if (directories.length !== expectedDirectories.size || directories.some((directory) => !expectedDirectories.has(directory))) {
		throw new Error(`Synthetic sample input root must contain exactly the catalog directories: ${root}`);
	}
	if (files.length !== expectedFiles.size || files.some((file) => !expectedFiles.has(file))) {
		throw new Error(`Synthetic sample input root must contain exactly the catalog files: ${root}`);
	}
	for (const entry of lock.files) {
		const relative = entry.path.slice('samples/'.length);
		const segments = relative.split('/');
		const file = path.join(root, ...segments);
		const stat = await lstat(file).catch(() => null);
		if (!stat?.isFile() || stat.isSymbolicLink()) throw new Error(`Required verified synthetic sample input is missing or linked: ${file}`);
		if (stat.size !== entry.bytes) throw new Error(`Synthetic sample input has the wrong size: ${file}`);
		const hash = createHash('sha256').update(await readFile(file)).digest('hex');
		if (hash !== entry.sha256) throw new Error(`Synthetic sample input failed SHA-256 verification: ${file}`);
	}
	return root;
}
