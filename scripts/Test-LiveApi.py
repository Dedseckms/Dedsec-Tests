"""Disposable, explicitly labelled API fixtures. Never use real user/hardware data."""
import argparse, json, urllib.request
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('mode',choices=['create','cleanup']);args=p.parse_args()
root=Path(__file__).resolve().parents[1];file=root/'.local'/'api-test-tokens.json';base='https://tests.dedseckms.ru'
def call(path,method='GET',body=None,token=None):
    headers={'User-Agent':'DedsecTests-QA/0.1'}
    if body is not None:headers['Content-Type']='application/json';body=json.dumps(body).encode()
    if token:headers['Authorization']='Bearer '+token
    with urllib.request.urlopen(urllib.request.Request(base+path,data=body,headers=headers,method=method),timeout=25) as r:return json.load(r)
if args.mode=='create':
    if file.exists():raise SystemExit('Clean previous fixtures first')
    tokens=[]
    try:
        for i in range(2):
            challenge=call('/api/challenge');body={'suite_version':'dedsec-v1','app_version':'0.1.0','profile':'standard','consent':True,'consent_version':'2026-11-23','nonce':challenge['nonce'],'system':{'cpu':'DEDSEC QA TEMPORARY FIXTURE '+str(i+1),'logical_cores':4,'gpu':'Synthetic API fixture — not a real benchmark','ram_gb':8,'windows':'Windows QA fixture — temporary test record','architecture':'X64','disks':[]},'measurements':[{'metric':'cpu_multi','value':100+i*10,'unit':'MiB/s','status':'ok','seconds':5,'detail':'Synthetic disposable API test fixture; not a hardware result'}]}
            result=call('/api/results','POST',body);tokens.append(result);file.parent.mkdir(exist_ok=True);file.write_text(json.dumps(tokens));detail=call('/api/results/'+result['id']);assert detail['system']==body['system'];assert 'delete_token' not in detail;print('Created disposable fixture:',result['id'])
        listing=call('/api/results?q=DEDSEC%20QA');assert listing['total']==2;print('Public listing, detail and schema verified')
    except BaseException:
        for item in tokens:call('/api/results/'+item['id'],'DELETE',token=item['delete_token'])
        file.unlink(missing_ok=True);raise
else:
    for item in json.loads(file.read_text()):assert call('/api/results/'+item['id'],'DELETE',token=item['delete_token'])['deleted']
    file.unlink();assert call('/api/results?q=DEDSEC%20QA')['total']==0;print('Disposable fixtures deleted; no synthetic benchmark retained')
