"""
Builds thunderstore-build/Flap-Good_Tidings-<version>.zip, the Thunderstore package, when the Thunderstore CLI (tcli) is not installed.
It holds what thunderstore.toml says it should: manifest.json, icon.png, the package README (with its
image links made absolute), CHANGELOG.md (both made by `modkit package`), and plugins/ with the DLL.

Run `dotnet build -c Release` and `modkit package <this folder>` first, then:  python thunderstore/make_zip.py
"""
import json, os, re, zipfile

here = os.path.dirname(os.path.abspath(__file__))
mod = os.path.dirname(here)
toml = open(os.path.join(mod, 'thunderstore.toml'), encoding='utf-8').read()


def field(name):
    return re.search(r'^%s\s*=\s*"([^"]*)"' % name, toml, re.M).group(1)


team, name, version = field('namespace'), field('name'), field('versionNumber')
manifest = {
    'name': name,
    'version_number': version,
    'website_url': field('websiteUrl'),
    'description': field('description'),
    'dependencies': [d + '-' + v for d, v in re.findall(r'^([\w]+-[\w]+)\s*=\s*"([^"]+)"', toml.split('[package.dependencies]')[1].split('[build]')[0], re.M)],
}

out_dir = os.path.join(mod, 'thunderstore-build')
out = os.path.join(out_dir, f'{team}-{name}-{version}.zip')
os.makedirs(out_dir, exist_ok=True)
if os.path.exists(out):
    os.remove(out)
with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
    z.writestr('manifest.json', json.dumps(manifest, indent=2))
    z.write(os.path.join(mod, 'thunderstore', 'icon.png'), 'icon.png')
    z.write(os.path.join(out_dir, 'README.md'), 'README.md')
    for src, target in re.findall(r'\[\[build\.copy\]\]\s*source = "([^"]+)"\s*target = "([^"]+)"', toml):
        z.write(os.path.join(mod, src), target)
print(out, os.path.getsize(out), 'bytes')
with zipfile.ZipFile(out) as z:
    for i in z.infolist():
        print(f'{i.file_size:>9}  {i.filename}')
