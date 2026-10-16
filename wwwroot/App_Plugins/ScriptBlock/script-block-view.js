import { LitElement, css, html, nothing } from '@umbraco-cms/backoffice/external/lit';

/**
 * Backoffice card for the Script Block. The snippet is never run in the backoffice (BlockPreview would
 * render it in its frame), so the card shows what it is instead: a code icon, the snippet's name and how
 * long it is. Wins over BlockPreview's generic view by weight (0 > -1000) and by naming the content type.
 */
export class KalasetScriptBlockViewElement extends LitElement {
  static properties = {
    content: { attribute: false },
    label: { attribute: false },
    config: { attribute: false },
    contentInvalid: { attribute: false },
  };

  render() {
    const name = this.content?.snippetName || 'Untitled snippet';
    const code = typeof this.content?.code === 'string' ? this.content.code.trim() : '';
    const lines = code ? code.split(/\r?\n/).length : 0;
    return html`
      <a class="card ${this.contentInvalid ? 'invalid' : ''}" href=${this.config?.editContentPath ?? nothing}>
        <span class="icon"><umb-icon name="icon-code"></umb-icon></span>
        <span class="text">
          <span class="name">${name}</span>
          <span class="meta">
            ${this.label ?? 'Script Block'} ·
            ${lines ? `${lines} ${lines === 1 ? 'line' : 'lines'} of HTML/JS` : 'No code yet'} ·
            runs on the published page only
          </span>
        </span>
      </a>`;
  }

  static styles = css`
    /* Same frame and spacing as the BlockPreview cards around it. */
    :host { display: block; margin-bottom: var(--uui-size-space-4); }
    .card {
      display: flex; align-items: center; gap: var(--uui-size-space-4);
      padding: var(--uui-size-space-4) var(--uui-size-space-5);
      border: 1px solid var(--uui-color-border); border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface); color: inherit; text-decoration: none;
    }
    .card:hover { border-color: var(--uui-color-border-emphasis); }
    .card:hover .name { text-decoration: underline; }
    .card.invalid { border-color: var(--uui-color-danger); }
    .icon {
      display: grid; place-items: center; flex: none;
      width: 48px; height: 48px; border-radius: var(--uui-border-radius);
      background: var(--uui-color-surface-alt); font-size: 24px;
    }
    .text { display: flex; flex-direction: column; gap: 2px; min-width: 0; }
    .name { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .meta { color: var(--uui-color-text-alt); font-size: 0.9em; }
  `;
}

customElements.define('kalaset-script-block-view', KalasetScriptBlockViewElement);
export default KalasetScriptBlockViewElement;
