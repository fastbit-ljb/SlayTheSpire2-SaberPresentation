"""Pack character-only resources from SHA-256 verified local source caches.

CZN contributes only the transitive Spine/texture/audio dependencies of the
selected effects. No foreign DLL, menu, HUD, currency or localization is shipped.
"""
import hashlib
import json
from pathlib import Path, PurePosixPath
import re

from catalogue_czn_assets import PACKS
from pck_tool import read_pck, extract, write_pck

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(4 * 1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def normalized(path):
    return path.removeprefix('res://')


def main():
    cache = ROOT / 'build/source-cache'
    previous = json.loads((cache / 'component-lock.json').read_text(encoding='utf-8'))
    cached_skin_nine = cache / 'standalone-source.pck'
    assert sha(cached_skin_nine) == previous['ownPack']['sha256'], 'Skin/Nine source cache checksum mismatch'
    legacy = read_pck(cached_skin_nine)
    core_path = ROOT / 'build/bundled/CznCore.pck'
    core_manifest = next(lib for lib in previous['libraries'] if lib['id'] == 'CznCore')
    assert sha(core_path) == next(f['sha256'] for f in core_manifest['files'] if f['file'].endswith('.pck')), 'Core source cache checksum mismatch'
    core = read_pck(core_path)
    # These packs are build inputs only. Runtime uses a single DLL and PCK.
    index = {normalized(p): (legacy, p) for p in legacy['files']}
    index.update({normalized(p): (core, p) for p in core['files']})
    for item in previous['nineEffects']['resources']:
        pack, path = index[item['path']]
        assert hashlib.sha256(extract(pack, path)).hexdigest() == item['sha256'], 'Nine effect checksum mismatch: ' + path

    own = read_pck(ROOT / 'build/SaberPresentation.pck')
    files = {normalized(p): extract(own, p) for p in own['files']}
    nine_cache_paths = {r['path'] for r in previous['nineEffects']['resources']}
    skin_files = [p for p in legacy['files'] if normalized(p).startswith(
        ('animations/', 'images/', '.godot/exported/', '.godot/imported/'))
        and normalized(p) not in nine_cache_paths and not normalized(p).startswith('.godot/imported/saber_')]
    assert len(skin_files) == previous['saberSkin']['includedResources'], 'Incomplete original Saber skin cache'
    for path in skin_files:
        files[normalized(path)] = extract(legacy, path)

    copied, missing = set(), set()
    def include(path):
        path = normalized(path)
        if path in copied:
            return
        # Exported loaders consume the import map even when the source pack
        # also contains raw atlas/skel/ogg files. Prefer that map explicitly.
        mapped = next((path + suffix for suffix in ('.import', '.remap') if path + suffix in index), None)
        if mapped:
            include(mapped)
            return
        if path not in index:
            missing.add(path)
            return
        copied.add(path)
        pack, packed = index[path]
        data = extract(pack, packed)
        files[path] = data
        if path.endswith('.spatlas'):
            atlas = json.loads(data)
            folder = PurePosixPath(normalized(atlas['source_path'])).parent
            for page in re.findall(r'(?m)^([^\r\n]+\.png)\r?$', atlas['atlas_data']):
                include(str(folder / page))
        if not path.endswith(('.ctex', '.spskel', '.oggvorbisstr')):
            for ref in re.findall(rb'res://[^\x00-\x1f"\x27]+', data):
                include(ref.decode('utf-8'))

    seeds = []
    for name, _, groups in PACKS:
        for prefix, suffix, names in groups:
            seeds.extend(name + '/' + prefix + filename + suffix for filename in names)
    for path in seeds:
        include(path)
    missing = {p for p in missing if p not in copied and p + '.import' not in copied and p + '.remap' not in copied}
    if missing:
        raise RuntimeError('Unresolved character effect dependencies: ' + repr(sorted(missing)))
    assert not any('CznStyleUI' in p or 'CznMainMenu' in p or 'CznEnemySkin' in p or '/localization/' in p for p in files)
    write_pck(ROOT / 'build/SaberPresentation.pck', files)
    resources = [{'path': p, 'sha256': hashlib.sha256(files[p]).hexdigest()} for p in sorted(copied)]
    lock = {
        'format': 2, 'externalModDependencies': [], 'nativeModEntries': 1,
        'runtimeAssemblies': ['SaberPresentation'], 'libraries': [],
        'nativeUiPreserved': True, 'localizationTablesMerged': 0,
        'excludedModules': ['CznStyleUI', 'CznMainMenu', 'CznEnemySkin', 'CznCore.dll', 'CznNineCharacter.dll'],
        'saberSkin': previous['saberSkin'],
        'effectSourcePacks': [
            {'id': 'CznCore', 'author': core_manifest['author'], 'version': core_manifest['version'], 'workshop': core_manifest['workshop'], 'sourceSha256': sha(core_path)},
            {'id': 'CznNineCharacter', 'author': previous['nineEffects']['author'], 'version': '2.0.0-beta', 'workshop': previous['nineEffects']['workshop'], 'sourceSha256': previous['nineEffects']['sourceSha256']}
        ],
        'characterEffects': {'seedResources': seeds, 'includesForeignAssembly': False, 'resources': resources},
        'ownPack': {'file': 'SaberPresentation.pck', 'resources': len(files), 'bytes': (ROOT / 'build/SaberPresentation.pck').stat().st_size, 'sha256': sha(ROOT / 'build/SaberPresentation.pck')}
    }
    (ROOT / 'STANDALONE_COMPONENTS.json').write_text(json.dumps(lock, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    from catalogue_czn_assets import main as catalogue_main
    catalogue_main()
    print(f'Character-only: {len(skin_files)} Saber skin files + {len(copied)} effect dependencies from {len(seeds)} seeds; zero foreign libraries')
    print(f"Own pack: {len(files)} resources, {lock['ownPack']['bytes']:,} bytes; original game UI retained")


if __name__ == '__main__':
    main()
