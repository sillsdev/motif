import json,sys,collections
d=json.load(open(sys.argv[1])); filt=sys.argv[2]; N=int(sys.argv[3]); mode=sys.argv[4] if len(sys.argv)>4 else 'incl'
fr=[f['name'] for f in d['shared']['frames']]
isSw=[('<Switch>d__2.MoveNext' in n) for n in fr]
isRJ=[('Dispatcher.RunJobs(value class System.Nullable`1<value class Avalonia.Threading.DispatcherPriority>)' in n) for n in fr]
isQ=[('<Quiet>' in n) for n in fr]
p=max(d['profiles'],key=lambda p:len(p['events']))
incl=collections.Counter(); stack=[]; last=None; total=0
for e in p['events']:
    t=e['at']
    if stack and last is not None:
        li=-1
        for k in range(len(stack)-1,-1,-1):
            if isSw[stack[k]]: li=k;break
        if li>=0:
            sub=stack[li:]
            if any(isRJ[i] for i in sub) and not any(isQ[i] for i in sub):
                dt=t-last; total+=dt
                if mode=='incl':
                    for i in set(sub):
                        if filt in fr[i]: incl[i]+=dt
                else:
                    # self: deepest non-CPU frame
                    j=len(sub)-1
                    while j>0 and fr[sub[j]] in('CPU_TIME','UNMANAGED_CODE_TIME','?!?'): j-=1
                    incl[sub[j]]+=dt
    last=t
    if e['type']=='O': stack.append(e['frame'])
    elif stack: stack.pop()
print('total',round(total))
for i,v in incl.most_common(N): print(f'{v:8.0f} {fr[i][:165]}')
