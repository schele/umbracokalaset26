// Gallery block: opens a thumbnail's full image in a lightbox (<dialog>) instead of following the link, with
// previous/next buttons and arrow keys within the same gallery. Escape (native to <dialog>) closes it and
// focus returns to the thumbnail. Without script the thumbnails are plain links to the images.
(() => {
	let dialog, image, caption, items = [], index = 0, opener = null;

	function build() {
		dialog = document.createElement('dialog');
		dialog.className = 'gallery-lightbox';
		dialog.setAttribute('aria-label', 'Image viewer');
		dialog.innerHTML = `
			<figure class="gallery-lightbox__figure"><img alt=""><figcaption class="gallery-lightbox__caption"></figcaption></figure>
			<button type="button" class="gallery-lightbox__prev" aria-label="Previous image">‹</button>
			<button type="button" class="gallery-lightbox__next" aria-label="Next image">›</button>
			<button type="button" class="gallery-lightbox__close" aria-label="Close">×</button>`;
		image = dialog.querySelector('img');
		caption = dialog.querySelector('figcaption');
		dialog.querySelector('.gallery-lightbox__prev').addEventListener('click', () => show(index - 1));
		dialog.querySelector('.gallery-lightbox__next').addEventListener('click', () => show(index + 1));
		dialog.querySelector('.gallery-lightbox__close').addEventListener('click', () => dialog.close());
		// A click on the dark area around the image closes it.
		dialog.addEventListener('click', (event) => {
			if (event.target === dialog || event.target.classList.contains('gallery-lightbox__figure')) dialog.close();
		});
		dialog.addEventListener('keydown', (event) => {
			if (event.key === 'ArrowLeft') show(index - 1);
			if (event.key === 'ArrowRight') show(index + 1);
		});
		dialog.addEventListener('close', () => opener?.focus());
		document.body.append(dialog);
	}

	function show(i) {
		index = (i + items.length) % items.length;
		const link = items[index];
		const alt = link.querySelector('img')?.alt ?? '';
		image.src = link.href;
		image.alt = alt;
		caption.textContent = items.length > 1 ? `${alt} (${index + 1} / ${items.length})` : alt;
		const single = items.length < 2;
		dialog.querySelector('.gallery-lightbox__prev').hidden = single;
		dialog.querySelector('.gallery-lightbox__next').hidden = single;
	}

	document.addEventListener('click', (event) => {
		const link = event.target.closest('[data-gallery-image]');
		if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
		event.preventDefault();
		if (!dialog) build();
		items = Array.from(link.closest('.gallery-block').querySelectorAll('[data-gallery-image]'));
		opener = link;
		show(items.indexOf(link));
		dialog.showModal();
	});
})();
