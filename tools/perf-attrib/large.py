import json,sys,statistics as st,collections
f=sys.argv[1]; lab=sys.argv[2]
rows=[json.loads(l) for l in open(f)]
fs={};tr={};sw=collections.defaultdict(list);q=collections.defaultdict(list)
fsl=collections.defaultdict(list)
for r in rows:
    if r.get('label')!=lab: continue
    if r['name']=='attrib.frame-split': fsl[r['view']].append(r)
    if r['name']=='attrib.tree': tr[r['view']]=r
    if r['name']=='attrib.settle': q[r['view']].append(r['quiet-ms'])
    if r['name']=='view.switch': sw[r['to']].append(r['ms'])
print(f"{'view':22}{'switch':>8}{'loop':>8}{'created':>8}{'vis-all':>8}{'quiet':>7}  owner")
for v,l in fsl.items():
    t=tr[v]
    print(f"{v:22}{st.median(x['loop-ms'] for x in l):8.0f}{max(x['loop-ms'] for x in l):8.0f}{t['created']:8}{t['visual-visible']:8}{st.median(q[v]):7.0f}  {t['created-owner'][:110]}")
