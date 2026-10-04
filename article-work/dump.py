import re,sys
x=open(sys.argv[1],encoding='utf-8').read()
body=x[x.index('<w:body>'):]
paras=re.findall(r'<w:p[ >].*?</w:p>|<w:tbl>.*?</w:tbl>',body,re.S)
for i,p in enumerate(paras):
    if p.startswith('<w:tbl'):
        print(i,'TABLE',re.sub(r'<[^>]+>','',p)[:120]); continue
    st=re.search(r'<w:pStyle w:val="([^"]+)"',p); st=st.group(1) if st else '-'
    t=''.join(re.findall(r'<w:t[^>]*>([^<]*)</w:t>',p))
    extra=' [IMG]' if '<w:drawing' in p else ''
    print(i,st+extra,'|',t[:int(sys.argv[2])])
