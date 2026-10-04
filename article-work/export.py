import re,html
x=open('d/word/document.xml',encoding='utf-8').read()
body=x[x.index('<w:body>'):]
paras=re.findall(r'<w:p[ >].*?</w:p>|<w:tbl>.*?</w:tbl>',body,re.S)
out=[]
for i,p in enumerate(paras[:160]):
    if p.startswith('<w:tbl'): continue
    st=re.search(r'<w:pStyle w:val="([^"]+)"',p); st=st.group(1) if st else '-'
    if st not in ('BodyText','NumberedBodyText','BullettedBodyText'): continue
    s=''
    for r in re.findall(r'<w:r>(.*?)</w:r>',p,re.S):
        t=html.unescape(''.join(re.findall(r'<w:t[^>]*>([^<]*)</w:t>',r)))
        if not t: continue
        if '<w:b/>' in r and 'w:sz w:val="56"' not in r: t='{b}'+t+'{/b}'
        elif '<w:i/>' in r: t='{i}'+t+'{/i}'
        s+=t
    if s.strip(): out.append(f'[{i}] {st}\n{s}\n')
open('body.md','w').write('\n'.join(out))
