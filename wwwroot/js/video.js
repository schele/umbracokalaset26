// Click-to-load YouTube: the player (and YouTube's scripts and cookies) loads only when the
// visitor presses play on a video block (Views/Partials/blocklist/Components/videoBlock.cshtml).
document.addEventListener('click', (event) => {
	const poster = event.target.closest?.('[data-youtube-id]');
	if (!poster) return;

	const params = new URLSearchParams({ autoplay: '1', rel: '0' });
	if (poster.dataset.youtubeStart) params.set('start', poster.dataset.youtubeStart);

	const player = document.createElement('iframe');
	player.className = 'video-block__player';
	player.src = `https://www.youtube-nocookie.com/embed/${encodeURIComponent(poster.dataset.youtubeId)}?${params}`;
	player.title = poster.getAttribute('aria-label') ?? 'YouTube video';
	player.allow = 'accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share';
	player.allowFullscreen = true;
	poster.replaceWith(player);
	player.focus();
});
