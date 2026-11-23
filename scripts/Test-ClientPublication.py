"""Real .NET publisher against the production Python handler in an isolated database."""
import argparse
import gc
from pathlib import Path
import subprocess
import sys
import tempfile
import threading

root=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(root/'server'))
import app

p=argparse.ArgumentParser();p.add_argument('--dotnet',default='dotnet');args=p.parse_args()
with tempfile.TemporaryDirectory() as folder:
    app.DB=Path(folder)/'results.sqlite3';app.initialize()
    server=app.Server(('127.0.0.1',0),app.Handler)
    worker=threading.Thread(target=server.serve_forever,daemon=True);worker.start()
    try:
        subprocess.run([args.dotnet,'run','--project',str(root/'tests/Dedsec.Tests.Checks'),'-c','Release','--','--publication-url',f'http://127.0.0.1:{server.server_port}/','--framing-probe'],cwd=root,check=True)
        connection=app.connect()
        try:assert connection.execute('SELECT COUNT(*) FROM results').fetchone()[0]==0
        finally:connection.close()
        print('Isolated registry empty after transport integration test')
    finally:
        server.shutdown();server.server_close();worker.join();gc.collect()
