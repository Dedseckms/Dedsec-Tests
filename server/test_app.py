import copy
import hashlib
import http.client
import json
from pathlib import Path
import tempfile
import threading
import unittest
import app

class RegistryTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp=tempfile.TemporaryDirectory();app.DB=Path(cls.tmp.name)/'results.sqlite3';app.initialize();cls.server=app.Server(('127.0.0.1',0),app.Handler);cls.thread=threading.Thread(target=cls.server.serve_forever,daemon=True);cls.thread.start()
    @classmethod
    def tearDownClass(cls):cls.server.shutdown();cls.server.server_close();cls.tmp.cleanup()
    def call(self,method,path,body=None,headers=None):
        c=http.client.HTTPConnection('127.0.0.1',self.server.server_port,timeout=10);h={'X-Real-IP':'test-'+self.id()};h.update(headers or {})
        if body is not None:body=json.dumps(body).encode();h['Content-Type']='application/json'
        c.request(method,path,body,h);r=c.getresponse();data=r.read();status=r.status;c.close()
        return status,json.loads(data)
    def payload(self):
        _,c=self.call('GET','/api/challenge')
        return {'suite_version':'dedsec-v1','app_version':'0.1.0','profile':'standard','consent':True,'consent_version':app.CONSENT,'nonce':c['nonce'],'system':{'cpu':'Example CPU','logical_cores':4,'gpu':'Example GPU','ram_gb':8,'windows':'Windows 10 test fixture','architecture':'X64','disks':[{'model':'Example SSD','media':'SSD','capacity_gb':256}]},'measurements':[{'metric':'cpu_multi','value':123.4,'unit':'MiB/s','status':'ok','detail':'synthetic unit-test fixture','seconds':5}]}
    def test_consent_required(self):
        p=self.payload();p['consent']=False;self.assertEqual(self.call('POST','/api/results',p)[0],400)
    def test_unknown_private_fields_rejected(self):
        for field in ('serial','username','ip','processes','hostname'):
            p=self.payload();p['system'][field]='secret';self.assertEqual(self.call('POST','/api/results',p)[0],400)
    def test_numeric_validation(self):
        for value in (True,-1,float('nan'),float('inf'),'2'):
            p=self.payload();p['measurements'][0]['value']=value;self.assertEqual(self.call('POST','/api/results',p)[0],400)
    def test_replay_detail_delete(self):
        p=self.payload();status,created=self.call('POST','/api/results',p);self.assertEqual(status,201)
        self.assertEqual(self.call('POST','/api/results',p)[0],400)
        path='/api/results/'+created['id'];status,row=self.call('GET',path);self.assertEqual(status,200);self.assertNotIn('delete_token',row);self.assertNotIn('nonce',row);self.assertNotIn('consent',row)
        self.assertEqual(self.call('DELETE',path,headers={'Authorization':'Bearer invalid'})[0],403)
        self.assertEqual(self.call('DELETE',path,headers={'Authorization':'Bearer '+created['delete_token']})[0],200);self.assertEqual(self.call('GET',path)[0],404)
        with app.connect() as c:self.assertEqual(c.execute('SELECT COUNT(*) FROM metrics WHERE result_id=?',(created['id'],)).fetchone()[0],0)
    def test_duplicate_metric_rejected(self):
        p=self.payload();p['measurements']*=2;self.assertEqual(self.call('POST','/api/results',p)[0],400)
    def test_no_stress_rank(self):
        p=self.payload();p['profile']='stress';self.assertEqual(self.call('POST','/api/results',p)[0],400)
    def test_injection_search_and_traversal(self):
        self.assertEqual(self.call('GET',"/api/results?q=%27%20OR%201%3D1--")[0],200)
        self.assertEqual(self.call('GET','/../app.py')[0],404)
    def test_backup(self):app.backup();self.assertEqual(len(list((app.DB.parent/'backups').glob('*.sqlite3'))),1)
    def test_bad_origin(self):self.assertEqual(self.call('POST','/api/results',self.payload(),{'Origin':'https://evil.example'})[0],403)
    def test_lost_response_retry_keeps_deletion_control(self):
        token='a'*64;p=self.payload();status,first=self.call('POST','/api/results',p,{'X-Deletion-Key':token});self.assertEqual(status,201)
        p['nonce']=self.payload()['nonce'];status,again=self.call('POST','/api/results',p,{'X-Deletion-Key':token});self.assertEqual(status,200);self.assertEqual(first['id'],again['id']);self.assertEqual(again['delete_token'],token)
        p['nonce']=self.payload()['nonce'];p['measurements'][0]['value']=999;self.assertEqual(self.call('POST','/api/results',p,{'X-Deletion-Key':token})[0],409)
        self.assertEqual(self.call('DELETE','/api/results/'+first['id'],headers={'Authorization':'Bearer '+token})[0],200)

if __name__=='__main__':unittest.main()
