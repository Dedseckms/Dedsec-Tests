#!/usr/bin/env python3
"""Dedsec Tests: bounded, anonymous, client-reported benchmark registry. Stdlib only."""
import argparse
import hashlib
import hmac
import json
import math
import os
from pathlib import Path
import re
import secrets
import sqlite3
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit, parse_qs

ROOT = Path(__file__).resolve().parent
DB = Path(os.environ.get('DEDSEC_DB', '/var/lib/dedsec-tests/results.sqlite3'))
SITE = 'https://tests.dedseckms.ru'
CONSENT = '2026-11-23'
METRICS = {'cpu_single': 'MiB/s', 'cpu_multi': 'MiB/s', 'gpu_compute': 'Miter/s', 'ram_copy': 'MiB/s', 'ram_errors': 'errors', 'disk_read': 'MiB/s', 'disk_write': 'MiB/s', 'disk_latency': 'ms'}
LOCK = threading.Lock()
NONCES = {}
RATES = {}
SALT = secrets.token_bytes(32)

def connect():
    conn = sqlite3.connect(DB, timeout=5)
    conn.row_factory = sqlite3.Row
    conn.execute('PRAGMA foreign_keys=ON')
    return conn

def initialize():
    DB.parent.mkdir(parents=True, exist_ok=True)
    with connect() as c:
        c.execute('PRAGMA journal_mode=WAL')
        c.executescript('''CREATE TABLE IF NOT EXISTS results (
            id TEXT PRIMARY KEY, created INTEGER NOT NULL, suite TEXT NOT NULL,
            cpu TEXT NOT NULL, gpu TEXT NOT NULL, windows TEXT NOT NULL,
            payload TEXT NOT NULL, delete_hash TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS results_created ON results(created DESC);
            CREATE UNIQUE INDEX IF NOT EXISTS result_deletion_key ON results(delete_hash);
            CREATE TABLE IF NOT EXISTS metrics (
            result_id TEXT NOT NULL REFERENCES results(id) ON DELETE CASCADE,
            metric TEXT NOT NULL, value REAL NOT NULL, PRIMARY KEY(result_id,metric));
            CREATE INDEX IF NOT EXISTS metrics_rank ON metrics(metric,value DESC);
        ''')

def keys(obj, expected):
    if not isinstance(obj, dict) or set(obj) != set(expected):
        raise ValueError('Unexpected or missing fields')

def label(value, maximum=200):
    if not isinstance(value, str) or not 1 <= len(value) <= maximum or any(ord(ch) < 32 or ord(ch) == 127 for ch in value):
        raise ValueError('Invalid text')
    return value

def number(value, low, high, integer=False):
    if type(value) not in (int, float) or not math.isfinite(value) or not low <= value <= high or integer and int(value) != value:
        raise ValueError('Invalid numeric value')
    return value

def validate(body):
    keys(body, ['suite_version','app_version','profile','system','measurements','consent','consent_version','nonce'])
    if body['consent'] is not True or body['consent_version'] != CONSENT:
        raise ValueError('Explicit publication consent required')
    if body['suite_version'] != 'dedsec-v1' or body['profile'] != 'standard' or not re.fullmatch(r'\d+\.\d+\.\d+', str(body['app_version'])):
        raise ValueError('Unsupported benchmark version or profile')
    s = body['system']
    keys(s, ['cpu','logical_cores','gpu','ram_gb','windows','architecture','disks'])
    for field in ('cpu','gpu','windows'): label(s[field], 600 if field == 'gpu' else 200)
    number(s['logical_cores'],1,2048,True); number(s['ram_gb'],1,65536,True)
    if s['architecture'] not in ('X64','X86','Arm64'): raise ValueError('Unsupported architecture')
    if not isinstance(s['disks'],list) or len(s['disks'])>16: raise ValueError('Too many drives')
    for disk in s['disks']:
        keys(disk,['model','media','capacity_gb']); label(disk['model']); label(disk['media'],80); number(disk['capacity_gb'],0,1000000,True)
    measurements=body['measurements']
    if not isinstance(measurements,list) or not 1<=len(measurements)<=8: raise ValueError('Invalid measurements')
    seen=set()
    for m in measurements:
        keys(m,['metric','value','unit','status','detail','seconds'])
        if m['metric'] not in METRICS or m['metric'] in seen or m['unit']!=METRICS[m['metric']]: raise ValueError('Invalid or duplicate metric')
        seen.add(m['metric']);number(m['value'],0,1e10);number(m['seconds'],0,3600);label(m['detail'],600)
        if m['status'] not in ('ok','skipped','failed'):raise ValueError('Invalid status')
        if m['status']=='ok' and m['metric']!='ram_errors' and (m['value']<=0 or m['seconds']<=0):raise ValueError('Empty successful measurement')
    if not any(m['status']=='ok' for m in measurements):raise ValueError('No completed measurements')
    # Persist only this explicit schema. No nonce, consent checkbox, network identifiers, logs, or local findings.
    return {k:body[k] for k in ('suite_version','app_version','profile','system','measurements')}

def rate(key, limit, period=3600):
    now=time.time()
    bucket=hmac.digest(SALT, f'{int(now//period)}:{key}'.encode(),'sha256')
    with LOCK:
        for k in list(RATES):
            if RATES[k][1]<now:del RATES[k]
        if len(RATES)>10000:return False
        count,end=RATES.get(bucket,(0,now+period))
        RATES[bucket]=(count+1,end)
        return count<limit

def result(row):
    body=json.loads(row['payload']);body.update(id=row['id'],created_utc=row['created'],verification='client-reported')
    return body

class Server(ThreadingHTTPServer):
    daemon_threads=True
    request_queue_size=32
    allow_reuse_address=True
    def __init__(self,*args):
        self.slots=threading.BoundedSemaphore(16)
        super().__init__(*args)
    def process_request(self,request,address):
        if not self.slots.acquire(False):request.close();return
        try:super().process_request(request,address)
        except BaseException:self.slots.release();raise
    def process_request_thread(self,request,address):
        try:super().process_request_thread(request,address)
        finally:self.slots.release()

class Handler(BaseHTTPRequestHandler):
    server_version='DedsecTests'
    sys_version=''
    protocol_version='HTTP/1.0'
    def setup(self):
        super().setup();self.connection.settimeout(10)
    def log_message(self,*args):pass  # no IP, URL, authorization, or request bodies in persistent logs
    def send(self,status,data,kind='application/json; charset=utf-8'):
        raw=json.dumps(data,ensure_ascii=False,allow_nan=False).encode() if kind.startswith('application/json') else data
        self.send_response(status);self.send_header('Content-Type',kind);self.send_header('Content-Length',str(len(raw)))
        self.send_header('X-Content-Type-Options','nosniff');self.send_header('Referrer-Policy','no-referrer')
        self.send_header('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'")
        self.send_header('Cache-Control','no-store' if kind.startswith('application/json') else 'no-cache')
        self.end_headers()
        if self.command!='HEAD':self.wfile.write(raw)
    def error(self,status,message):self.send(status,{'error':message})
    def identity(self):
        # Loopback-only backend is reachable through our own Caddy tunnel; Caddy overrides X-Real-IP.
        return self.headers.get('X-Real-IP',self.client_address[0])[:128]
    def do_HEAD(self):self.do_GET()
    def do_GET(self):
        try:self.get()
        except (BrokenPipeError,ConnectionResetError,TimeoutError):pass
        except (ValueError,sqlite3.Error):self.error(400,'Invalid query')
    def get(self):
        parsed=urlsplit(self.path);path=parsed.path;q=parse_qs(parsed.query)
        if path=='/api/health':return self.send(200,{'status':'ok','suite':'dedsec-v1'})
        if path=='/api/challenge':
            if not rate('nonce:'+self.identity(),30):return self.error(429,'Try later')
            nonce=secrets.token_urlsafe(24);now=time.time()
            with LOCK:
                for k in list(NONCES):
                    if NONCES[k]<now:del NONCES[k]
                if len(NONCES)>=10000:return self.error(503,'Busy')
                NONCES[nonce]=now+300
            return self.send(200,{'nonce':nonce,'consent_version':CONSENT,'expires_in':300})
        if path=='/api/stats':
            with connect() as c:
                total=c.execute('SELECT COUNT(*) FROM results').fetchone()[0]
                cpus=c.execute('SELECT COUNT(DISTINCT cpu) FROM results').fetchone()[0]
                gpus=c.execute('SELECT COUNT(DISTINCT gpu) FROM results').fetchone()[0]
            return self.send(200,{'results':total,'cpus':cpus,'gpus':gpus,'suite':'dedsec-v1'})
        if path=='/api/results':
            metric=q.get('metric',['cpu_multi'])[0]
            if metric not in METRICS:raise ValueError()
            offset=max(0,min(100000,int(q.get('offset',['0'])[0])))
            search=q.get('q',[''])[0][:100];search=search.replace('\\','\\\\').replace('%','\\%').replace('_','\\_')
            where="r.suite='dedsec-v1' AND (r.cpu LIKE ? ESCAPE '\\' OR r.gpu LIKE ? ESCAPE '\\' OR r.windows LIKE ? ESCAPE '\\')"
            params=['%'+search+'%']*3
            direction='ASC' if metric in ('disk_latency','ram_errors') else 'DESC'
            with connect() as c:
                total=c.execute(f'SELECT COUNT(*) FROM results r WHERE {where}',params).fetchone()[0]
                rows=c.execute(f'SELECT r.* FROM results r LEFT JOIN metrics m ON m.result_id=r.id AND m.metric=? WHERE {where} ORDER BY (m.value IS NULL),m.value {direction},r.created DESC LIMIT 30 OFFSET ?', [metric,*params,offset]).fetchall()
            return self.send(200,{'items':[result(r) for r in rows],'total':total,'offset':offset,'limit':30})
        if re.fullmatch('/api/results/[a-f0-9]{24}',path):
            with connect() as c:row=c.execute('SELECT * FROM results WHERE id=?',(path.rsplit('/',1)[1],)).fetchone()
            return self.send(200,result(row)) if row else self.error(404,'Not found')
        mapping={'/':'index.html','/privacy':'privacy.html','/methodology':'methodology.html'}
        if re.fullmatch('/result/[a-f0-9]{24}',path):name='index.html'
        elif path in mapping:name=mapping[path]
        elif re.fullmatch('/(app.js|style.css|icon.png|favicon.ico)',path):name=path[1:]
        else:return self.error(404,'Not found')
        mime={'.html':'text/html; charset=utf-8','.js':'text/javascript; charset=utf-8','.css':'text/css; charset=utf-8','.png':'image/png','.ico':'image/x-icon'}[Path(name).suffix]
        return self.send(200,(ROOT/'web'/name).read_bytes(),mime)
    def do_POST(self):
        if self.path!='/api/results':return self.error(404,'Not found')
        try:
            if self.headers.get('Transfer-Encoding'):return self.error(400,'Transfer encoding unsupported')
            size=int(self.headers.get('Content-Length','0'))
            if size<1 or size>32768:return self.error(413,'Body size limit: 32 KiB')
            if self.headers.get('Content-Type','').split(';')[0]!='application/json':return self.error(415,'JSON required')
            if self.headers.get('Origin') not in (None,SITE):return self.error(403,'Origin rejected')
            if not rate('post:'+self.identity(),12):return self.error(429,'Publication limit reached')
            def unique(pairs):
                out={}
                for k,v in pairs:
                    if k in out:raise ValueError('Duplicate JSON key')
                    out[k]=v
                return out
            body=json.loads(self.rfile.read(size),object_pairs_hook=unique)
            payload=validate(body)
            with LOCK:expires=NONCES.pop(body['nonce'],0)
            if expires<time.time():return self.error(400,'Consent challenge expired; retry publication')
            token=self.headers.get('X-Deletion-Key') or secrets.token_urlsafe(32)
            if not re.fullmatch(r'[A-Za-z0-9_-]{43,128}',token):return self.error(400,'Invalid deletion key')
            token_hash=hashlib.sha256(token.encode()).hexdigest()
            identifier=secrets.token_hex(12);created=int(time.time())
            with connect() as c:
                previous=c.execute('SELECT id,payload FROM results WHERE delete_hash=?',(token_hash,)).fetchone()
                if previous:
                    if json.loads(previous['payload'])!=payload:return self.error(409,'Key already belongs to another report')
                    return self.send(200,{'id':previous['id'],'url':SITE+'/result/'+previous['id'],'delete_token':token})
                if c.execute('SELECT COUNT(*) FROM results').fetchone()[0]>=100000:return self.error(507,'Registry capacity reached')
                c.execute('INSERT INTO results VALUES (?,?,?,?,?,?,?,?)',(identifier,created,payload['suite_version'],payload['system']['cpu'],payload['system']['gpu'],payload['system']['windows'],json.dumps(payload,ensure_ascii=False,allow_nan=False),token_hash))
                c.executemany('INSERT INTO metrics VALUES (?,?,?)',[(identifier,m['metric'],m['value']) for m in payload['measurements'] if m['status']=='ok'])
            return self.send(201,{'id':identifier,'url':SITE+'/result/'+identifier,'delete_token':token})
        except (ValueError,TypeError,KeyError):self.error(400,'Invalid publication schema or values')
        except sqlite3.Error:self.error(503,'Storage unavailable; local report is safe')
        except (TimeoutError,BrokenPipeError,ConnectionResetError):pass
    def do_DELETE(self):
        path=urlsplit(self.path).path
        if not re.fullmatch('/api/results/[a-f0-9]{24}',path):return self.error(404,'Not found')
        if not rate('delete:'+self.identity(),30):return self.error(429,'Try later')
        auth=self.headers.get('Authorization','')
        if not auth.startswith('Bearer ') or len(auth)>200:return self.error(403,'Deletion key required')
        hashed=hashlib.sha256(auth[7:].encode()).hexdigest()
        try:
            with connect() as c:
                row=c.execute('SELECT delete_hash FROM results WHERE id=?',(path.rsplit('/',1)[1],)).fetchone()
                if not row or not hmac.compare_digest(hashed,row[0]):return self.error(403,'Invalid deletion key')
                c.execute('DELETE FROM results WHERE id=?',(path.rsplit('/',1)[1],))
            return self.send(200,{'deleted':True})
        except sqlite3.Error:self.error(503,'Storage unavailable')

def backup():
    folder=DB.parent/'backups';folder.mkdir(exist_ok=True)
    path=folder/time.strftime('results-%Y%m%d-%H%M%S.sqlite3',time.gmtime())
    with connect() as source,sqlite3.connect(path) as dest:source.backup(dest)
    os.chmod(path,0o600)
    for old in sorted(folder.glob('results-*.sqlite3'))[:-7]:old.unlink()

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--host',default='127.0.0.1');parser.add_argument('--port',type=int,default=18240);parser.add_argument('--backup',action='store_true');args=parser.parse_args();initialize()
    if args.backup:backup()
    else:Server((args.host,args.port),Handler).serve_forever()
