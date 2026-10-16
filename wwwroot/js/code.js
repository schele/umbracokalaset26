// Code block: the copy button in the top right corner (Views/Partials/blocklist/Components/codeBlock.cshtml).
// Copies the code as written: one line per .code-line, without the CSS line numbers.
(() => {
	const RESET_AFTER_MS = 2000;

	const codeOf = (block) =>
		Array.from(block.querySelectorAll('.code-line'), (line) => line.textContent).join('\n');

	// The Clipboard API needs a secure context and permission; a hidden textarea + execCommand covers the rest
	// (e.g. plain http on a test server).
	async function copy(text) {
		try {
			await navigator.clipboard.writeText(text);
			return true;
		} catch {
			const area = document.createElement('textarea');
			area.value = text;
			area.setAttribute('readonly', '');
			area.style.cssText = 'position:fixed;top:0;left:0;opacity:0;';
			document.body.append(area);
			area.select();
			try {
				return document.execCommand('copy');
			} catch {
				return false;
			} finally {
				area.remove();
			}
		}
	}

	document.addEventListener('click', async (event) => {
		const button = event.target.closest('.code-block__copy');
		if (!button) return;

		const ok = await copy(codeOf(button.closest('.code-block')));
		const label = button.querySelector('.code-block__copy-label');
		button.classList.toggle('is-copied', ok);
		label.textContent = ok ? 'Copied' : 'Copy failed';
		clearTimeout(button.resetTimer);
		button.resetTimer = setTimeout(() => {
			button.classList.remove('is-copied');
			label.textContent = 'Copy';
		}, RESET_AFTER_MS);
	});

	document.querySelectorAll('.code-block__copy').forEach((button) => { button.hidden = false; });
})();
