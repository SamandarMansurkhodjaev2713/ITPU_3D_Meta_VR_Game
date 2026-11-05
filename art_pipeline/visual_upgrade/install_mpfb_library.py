"""Install reviewed official CC0 data assets offline, preserving existing files."""
import bpy, importlib, hashlib, json, zipfile
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
module='bl_ext.user_default.mpfb'
if bpy.app.online_access: raise RuntimeError('Use --offline-mode')
import addon_utils
if module not in bpy.context.preferences.addons:
    addon_utils.enable(module, default_set=True, persistent=False)
from bl_ext.user_default.mpfb.services import LocationService, AssetService
archive=ROOT/'reports/local/vendor/mpfb_system_assets/makehuman_system_assets_cc0.zip'
destination=Path(LocationService.get_user_data()).resolve()
with zipfile.ZipFile(archive) as pack:
    entries=pack.infolist()
    for entry in entries:
        path=(destination/entry.filename).resolve()
        if not path.is_relative_to(destination): raise RuntimeError('Invalid archive path')
        if path.suffix.lower() in {'.exe','.dll','.py','.ps1','.bat','.cmd','.js'}:
            raise RuntimeError('Executable in data-only asset pack: '+entry.filename)
        if entry.is_dir(): continue
        data=pack.read(entry)
        if path.exists() and path.read_bytes()!=data:
            raise RuntimeError('Existing user asset differs; preserve it: '+entry.filename)
    for entry in entries:
        path=(destination/entry.filename).resolve()
        if entry.is_dir(): path.mkdir(parents=True,exist_ok=True)
        elif not path.exists():
            path.parent.mkdir(parents=True,exist_ok=True); path.write_bytes(pack.read(entry))
AssetService.rescan_pack_metadata()
assert AssetService.check_if_modern_makehuman_system_assets_installed()==(True,True)
clothes=sorted(p.relative_to(destination).as_posix() for p in destination.rglob('*.mhclo'))
proxies=sorted(p.relative_to(destination).as_posix() for p in destination.rglob('*.proxy'))
(ROOT/'reports/local/WO-31/mpfb_library_receipt.json').write_text(json.dumps({
    'status':'PASS_INSTALLED_AND_RECOGNIZED_OFFLINE',
    'source':'https://files2.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip',
    'source_page':'https://static.makehumancommunity.org/assets/assetpacks/makehuman_system_assets.html',
    'license':'CC0-1.0','archive_sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),
    'archive_files':len(entries),'clothes':clothes,'proxies':proxies,
    'private_media_uploaded':False},indent=2)+'\n')
print('MPFB SYSTEM ASSETS PASS',len(clothes),'clothes',len(proxies),'proxies')
