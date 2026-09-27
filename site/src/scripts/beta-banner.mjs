const storageKey = 'motif-beta-banner-dismissed';

function browserStorage() {
	try {
		return globalThis.localStorage;
	} catch {
		return null;
	}
}

export function initializeBetaBanners(documentRef, storage = browserStorage()) {
	let dismissed = false;
	try {
		dismissed = storage?.getItem(storageKey) === 'true';
	} catch {
		dismissed = false;
	}

	for (const banner of documentRef.querySelectorAll('[data-beta-banner]')) {
		banner.hidden = dismissed;
		if (dismissed) documentRef.documentElement.classList.add('beta-banner-dismissed');

		const button = banner.querySelector('[data-beta-dismiss]');
		button?.addEventListener('click', () => {
			banner.hidden = true;
			documentRef.documentElement.classList.add('beta-banner-dismissed');
			try {
				storage?.setItem(storageKey, 'true');
			} catch {
				return;
			}
		});
	}
}
