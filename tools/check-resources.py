# XAML ve C# içinde adıyla istenen kaynakların (StaticResource / DynamicResource / FindResource / Res)
# App.xaml'de ya da aynı dosyada tanımlı olup olmadığını denetler. WPF bunları ancak çalışma anında
# çözer; eksik bir StaticResource uygulamayı açılışta çökertir, derleme ise bunu yakalamaz.
# Çalıştır: python tools/check-resources.py   (hata varsa çıkış kodu 1)
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SKIP = ("Mobile", "bin", "obj", "tools", ".git")


def files(pattern):
    return [p for p in ROOT.rglob(pattern) if not any(part in SKIP for part in p.relative_to(ROOT).parts)]


def keys_in(text):
    return set(re.findall(r'x:Key="([^"]+)"', text))


app_keys = keys_in((ROOT / "App.xaml").read_text(encoding="utf-8-sig"))
# C# tarafında çalışma anında eklenen anahtarlar: res["X"] = ...
runtime_keys = set()
for cs in files("*.cs"):
    runtime_keys |= set(re.findall(r'res\["([^"]+)"\]\s*=', cs.read_text(encoding="utf-8-sig")))

problems = []
for xaml in files("*.xaml"):
    text = xaml.read_text(encoding="utf-8-sig")
    local = keys_in(text)
    for kind, key in re.findall(r"\{(StaticResource|DynamicResource)\s+([^}\s]+)\}", text):
        if key in app_keys or key in local:
            continue
        if kind == "DynamicResource" and key in runtime_keys:
            continue
        line = text[: text.find(key)].count("\n") + 1
        problems.append(f"{xaml.name}:{line}: {kind} '{key}' tanımlı değil")
    # StaticResource, tanımından önce kullanılırsa da çöker (aynı sözlükte sıra önemli).
    if xaml.name == "App.xaml":
        for m in re.finditer(r"\{StaticResource\s+([^}\s]+)\}", text):
            d = text.find(f'x:Key="{m.group(1)}"')
            if d > m.start():
                line = text[: m.start()].count("\n") + 1
                problems.append(f"App.xaml:{line}: StaticResource '{m.group(1)}' tanımından önce kullanılmış")

for cs in files("*.cs"):
    text = cs.read_text(encoding="utf-8-sig")
    for m in re.finditer(r'(?:FindResource|Res|TryFindResource|SetResourceReference\([^,]+,)\s*\(?\s*"([^"]+)"', text):
        key = m.group(1)
        if key not in app_keys and key not in runtime_keys:
            line = text[: m.start()].count("\n") + 1
            problems.append(f"{cs.name}:{line}: kaynak '{key}' App.xaml'de tanımlı değil")

for p in problems:
    print(p)
print(f"{len(problems)} sorun" if problems else "kaynaklar tamam")
sys.exit(1 if problems else 0)
