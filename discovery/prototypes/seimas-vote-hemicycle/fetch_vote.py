import subprocess,re,sys,json,html
bid=sys.argv[1]
B="https://apps.lrs.lt/sip/"
import time
for _ in range(5):
    r=subprocess.run(["curl","-sS","-m","30",B+f"p2b.ad_sp_balsavimo_rezultatai?balsavimo_id={bid}"],capture_output=True,text=True).stdout
    if "<BendriBalsavimoRezultatai" in r: break
    time.sleep(3)
tot=dict(re.findall(r'(\w+|už|prieš|susilaikė)="([^"]*)"',re.search(r'<BendriBalsavimoRezultatai[^>]*>',r).group(0)))
mps=[dict(n=a+" "+b,f=f,v=v) for a,b,f,v in re.findall(r'vardas="([^"]*)" pavardė="([^"]*)" frakcija="([^"]*)" kaip_balsavo="([^"]*)"',r)]
from collections import Counter,OrderedDict
size=Counter(m["f"] for m in mps)
order=[f for f,_ in size.most_common() if f!="MG"]+(["MG"] if "MG" in size else [])
data=[{"f":f,"mps":[m for m in mps if m["f"]==f]} for f in order]
json.dump({"bid":bid,"tot":tot,"data":data},open(f"vote_{bid}.json","w"),ensure_ascii=False)
print(tot); print({f:Counter(m["v"] or "nebalsavo" for m in mps if m["f"]==f) for f in order})
