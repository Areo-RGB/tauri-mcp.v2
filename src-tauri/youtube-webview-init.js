(() => {
  if (!location.hostname.endsWith('youtube.com') || window.__MCPHUB_CLIPPER_V2__) return;
  window.__MCPHUB_CLIPPER_V2__ = true;
  const ITEM = 'ytd-macro-markers-list-item-renderer, yt-list-item-view-model, [role="listitem"]';
  const selected = new Set();
  let videoId = '';
  const appendIcon = node => {
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('aria-hidden', 'true');
    const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    path.setAttribute('d', 'M12 3v12m0 0 5-5m-5 5-5-5M5 20h14');
    svg.append(path);
    node.append(svg);
  };

  const style = document.createElement('style');
  style.textContent = `.mh-select{display:flex;align-items:center;justify-content:center;flex:0 0 38px;align-self:stretch}.mh-check{appearance:none;width:18px;height:18px;border:2px solid var(--yt-spec-icon-inactive,#606060);border-radius:4px;cursor:pointer;position:relative}.mh-check:checked{background:#065fd4;border-color:#065fd4}.mh-check:checked:after{content:'✓';position:absolute;inset:-3px 0 0;color:#fff;text-align:center;font:700 15px/18px Arial}.mh-row,.mh-download{border:0;cursor:pointer;color:var(--yt-spec-text-primary,#0f0f0f)}.mh-row{display:grid;place-items:center;flex:0 0 36px;width:36px;height:36px;margin:auto 8px auto 0;border-radius:18px;background:transparent}.mh-row:hover{background:var(--yt-spec-badge-chip-background,#e5e5e5)}.mh-download{display:inline-flex;align-items:center;gap:6px;height:32px;padding:0 12px;margin-left:8px;border-radius:8px;background:#065fd4;color:#fff;font:600 13px Roboto,Arial,sans-serif}.mh-download:disabled{opacity:.6}.mh-row svg,.mh-download svg{width:18px;height:18px;fill:none;stroke:currentColor;stroke-width:2}.mh-toast{position:fixed;right:24px;bottom:24px;z-index:999999;padding:12px 16px;border-radius:8px;background:#212121;color:#fff;font:500 13px Roboto,Arial,sans-serif;box-shadow:0 4px 16px #0005;max-width:420px}`;

  const time = value => { const m=value?.trim().match(/^(?:(\d+):)?(\d+):(\d+)$/); return m ? Number(m[1]||0)*3600+Number(m[2])*60+Number(m[3]) : null };
  const start = item => {
    const link=item.querySelector('a[href*="t="],a[href*="start="]');
    if(link) try { const u=new URL(link.href), n=Number(String(u.searchParams.get('t')||u.searchParams.get('start')).replace(/s$/,'')); if(Number.isFinite(n)) return n } catch {}
    for(const node of item.querySelectorAll('span,yt-formatted-string')) { const n=time(node.textContent); if(n!==null) return n }
    return null;
  };
  const title = item => item.querySelector('#title,.macro-markers,h4,[class*="title"]')?.textContent?.trim() || [...item.querySelectorAll('span,yt-formatted-string')].map(n=>n.textContent?.trim()).find(v=>v&&time(v)===null&&v.length>1) || 'Chapter';
  const chapterItems = () => {
    const explicit=[...document.querySelectorAll(ITEM)].filter(item=>start(item)!==null);
    if(explicit.length)return [...new Set(explicit)];
    const panel=[...document.querySelectorAll('ytd-engagement-panel-section-list-renderer, [target-id*="engagement-panel"], [role="dialog"]')]
      .find(node=>/\bchapters\b|in this video/i.test(node.textContent||''));
    if(!panel)return [];
    const rows=[...panel.querySelectorAll('a[href*="t="], a[href*="start="]')].map(link=>{
      let node=link;
      while(node&&node.parentElement!==panel){
        if(node.matches?.('ytd-macro-markers-list-item-renderer, yt-list-item-view-model, [role="listitem"]'))return node;
        if(node.querySelector?.('img')&&(node.textContent?.match(/\b\d{1,2}:\d{2}(?::\d{2})?\b/)))return node;
        node=node.parentElement;
      }
      return link.parentElement;
    }).filter(Boolean);
    return [...new Set(rows)];
  };
  const chapters = () => {
    const items=chapterItems(), starts=items.map(start), duration=document.querySelector('video')?.duration||0;
    return items.map((item,i)=>{const s=starts[i],e=starts.slice(i+1).find(v=>v!==null&&v>s)??duration;return s===null||!Number.isFinite(e)||e<=s?null:{item,index:i+1,title:title(item),startTime:s,endTime:e,duration:e-s}}).filter(Boolean);
  };
  const toast = (text, permanent=false) => { document.querySelector('.mh-toast')?.remove(); const node=document.createElement('div');node.className='mh-toast';node.textContent=text;document.body.appendChild(node);if(!permanent)setTimeout(()=>node.remove(),7000);return node };
  async function process(list,button) {
    if(!list.length)return toast('Select at least one chapter.');
    const old=button?[...button.childNodes].map(node=>node.cloneNode(true)):[];if(button){button.disabled=true;button.textContent=`Processing ${list.length}…`}
    const note=toast('Downloading video and cutting selected chapters…',true);
    try {
      const clean=list.map(({item,...chapter})=>chapter);
      const result=await window.__TAURI_INTERNALS__.invoke('process_youtube_video',{url:location.href,chapters:clean});
      note.textContent=`${result.clips.length} clip${result.clips.length===1?'':'s'} saved to ${result.outputDir}`;setTimeout(()=>note.remove(),10000);
    } catch(error){note.textContent=`Chapter Clipper: ${error}`;setTimeout(()=>note.remove(),10000)} finally{if(button){button.disabled=false;button.replaceChildren(...old)}}
  }
  function inject() {
    const id=new URL(location.href).searchParams.get('v')||'';if(id!==videoId){videoId=id;selected.clear()}
    const list=chapters();if(!list.length)return;
    for(const chapter of list){const row=chapter.item.querySelector('#endpoint,a')||chapter.item;
      if(!chapter.item.querySelector('.mh-select')){const label=document.createElement('label');label.className='mh-select';label.title='Include this chapter';const check=document.createElement('input');check.type='checkbox';check.className='mh-check';check.checked=selected.has(chapter.startTime);check.onclick=e=>e.stopPropagation();check.onchange=()=>check.checked?selected.add(chapter.startTime):selected.delete(chapter.startTime);label.append(check);row.prepend(label)}
      if(!chapter.item.querySelector('.mh-row')){const button=document.createElement('button');button.className='mh-row';button.title='Download this chapter';button.setAttribute('aria-label',`Download ${chapter.title}`);appendIcon(button);button.onclick=e=>{e.preventDefault();e.stopPropagation();process([chapter],button)};row.append(button)}
    }
    const panel=list[0].item.closest('ytd-engagement-panel-section-list-renderer')||list[0].item.parentElement;if(!panel||panel.querySelector('.mh-download'))return;
    const transcript=[...panel.querySelectorAll('button,tp-yt-paper-tab,yt-button-shape')].find(n=>n.textContent?.trim().toLowerCase()==='transcript');const anchor=transcript?.parentElement||panel.querySelector('#header,#tabs-container,[role="tablist"]');if(!anchor)return;
    const button=document.createElement('button');button.className='mh-download';appendIcon(button);const label=document.createElement('span');label.textContent='Download';button.append(label);button.title='Download selected chapters';button.onclick=()=>process(chapters().filter(c=>selected.has(c.startTime)),button);transcript?transcript.insertAdjacentElement('afterend',button):anchor.append(button);
  }
  function mount() {
    document.documentElement.appendChild(style);
    let queued=false;
    new MutationObserver(()=>{if(!queued){queued=true;requestAnimationFrame(()=>{queued=false;inject()})}}).observe(document.documentElement,{childList:true,subtree:true});
    inject();
  }
  if (document.documentElement) mount();
  else {
    const rootObserver = new MutationObserver(() => {
      if (!document.documentElement) return;
      rootObserver.disconnect();
      mount();
    });
    rootObserver.observe(document, { childList: true, subtree: true });
  }
})();
