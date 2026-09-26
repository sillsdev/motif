export function getActiveStepIndex(steps, currentTimeSeconds) {
	if (!steps.length) return -1;
	const timeMs = currentTimeSeconds * 1000;
	const index = steps.findIndex((step) => step.startMs <= timeMs && timeMs < step.endMs);
	if (index >= 0) return index;
	return timeMs < steps[0].startMs ? 0 : steps.length - 1;
}

export function getNextStepIndex(steps, currentIndex) {
	if (!steps.length) return -1;
	return (currentIndex + 1) % steps.length;
}

export function getSeekTime(steps, index) {
	if (!steps.length) return 0;
	const safeIndex = Math.max(0, Math.min(index, steps.length - 1));
	return steps[safeIndex].startMs / 1000;
}
