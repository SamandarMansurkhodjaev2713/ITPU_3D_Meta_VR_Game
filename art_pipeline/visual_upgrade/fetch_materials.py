"""Download a bounded CC0 material set; no local/private input is transmitted."""
from pathlib import Path
import hashlib, json, urllib.request

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'unity/ITPU_VR_Unity/Assets/ITPU/Art/VisualUpgrade001/Textures'
REPORT = ROOT / 'reports/local/WO-31/material_sources.json'
OUT.mkdir(parents=True, exist_ok=True)
rows = []
for asset in ['wood_table_001', 'concrete_floor_02', 'fabric_pattern_05']:
    request = urllib.request.Request('https://api.polyhaven.com/files/' + asset,
        headers={'User-Agent': 'ITPU-VR-local-material-preparation/1.0'})
    with urllib.request.urlopen(request, timeout=45) as response:
        data = json.load(response)
    for channel in ['Diffuse', 'nor_gl', 'Rough']:
        source_channel = 'col_01' if asset == 'fabric_pattern_05' and channel == 'Diffuse' else channel
        item = data[source_channel]['1k']['jpg']
        filename = item['url'].rsplit('/', 1)[-1]
        path = OUT / filename
        if not path.exists():
            with urllib.request.urlopen(item['url'], timeout=60) as response:
                path.write_bytes(response.read())
        content = path.read_bytes()
        if hashlib.md5(content).hexdigest() != item['md5']:
            raise RuntimeError('Downloaded material checksum mismatch: ' + filename)
        rows.append({'asset': asset, 'channel': channel, 'url': item['url'],
            'license': 'CC0-1.0', 'source': 'https://polyhaven.com/a/' + asset,
            'file': path.relative_to(ROOT).as_posix(), 'bytes': len(content),
            'sha256': hashlib.sha256(content).hexdigest()})
        print(filename, len(content), flush=True)
REPORT.write_text(json.dumps({'status': 'PASS_DOWNLOADED_CHECKSUM_VERIFIED',
    'credit': 'Powered by Poly Haven', 'api': 'https://api.polyhaven.com',
    'license': 'https://polyhaven.com/license', 'maps': rows}, indent=2)+'\n', encoding='utf-8')
