// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/**
 * Gets the content property of the specified HTML <meta> tag
 */
export function meta(name: string): string {
  return (document.querySelector(`meta[name="${name}"]`) as HTMLMetaElement)?.content
}

/**
 * Injects <wbr> into long word in HTML elements.
 */
export function breakText() {
  document.querySelectorAll('.xref, .text-break').forEach(e => {
    if (!e.innerHTML.match(/(<\w*)((\s\/>)|(.*<\/\w*>))/g)) {
      e.innerHTML = breakPlainText(e.innerHTML)
    }
  })
}

function breakPlainText(text: string): string {
  return text.replace(/([a-z])([A-Z])|(\.)(\w)/g, '$1$3<wbr>$2$4')
}
