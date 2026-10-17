#!/usr/bin/env python3
"""
Static consistency checks for Coreloom.App's XAML that can run anywhere (no Windows, no WPF build).

The real verification is the Windows build (the XAML compiler) plus running the app; this script exists to
catch the common mistakes earlier and on any OS:

  * well-formed XML, and x:Class matches a partial class in the code-behind
  * StaticResource keys are defined before use (merge order respected), DynamicResource keys exist
    (including the brushes ThemeService creates at runtime), and runtime brushes are never used as
    StaticResource
  * event handler names in XAML exist in the code-behind
  * ElementName references and x:Name fields used from code-behind exist
  * every {Binding} path segment exists as a public property somewhere in the view models / core models
    or is a known WPF property (catches typos, not type mismatches)
  * icon keys referenced from view models exist, and pack URIs point at files embedded as Resource
  * windows that run on their own UI thread (the splash) never reference App.xaml resources
  * no array-typed dependency properties (the XAML compiler rejects them inside templates)

Usage: python3 build/check-xaml.py            (exit code 1 when problems are found)
"""
from __future__ import annotations

import fnmatch
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
APP = ROOT / "src" / "Coreloom.App"
X = "{http://schemas.microsoft.com/winfx/2006/xaml}"

# Merge order of App.xaml; Templates.xaml merges Icons/Converters/Controls itself.
GLOBAL_ORDER = ["Themes/Icons.xaml", "Themes/Converters.xaml", "Themes/Controls.xaml", "Themes/Templates.xaml"]
VIEWS = sorted((APP / "Views").glob("*.xaml"))

# Windows that run on their own UI thread (the startup splash). App.xaml's resources belong to the main thread, so
# these must define every resource they use and block resource lookups past themselves in code.
SELF_CONTAINED_VIEWS = {"SplashWindow.xaml"}

EVENT_ATTRS = {
    "Click", "Loaded", "Unloaded", "KeyDown", "KeyUp", "PreviewKeyDown", "SelectionChanged", "MouseDown", "MouseUp",
    "MouseLeftButtonDown", "MouseLeftButtonUp", "Checked", "Unchecked", "TextChanged", "SizeChanged", "Closing",
    "Closed", "GotFocus", "LostFocus", "PreviewMouseDown", "MouseEnter", "MouseLeave", "ValueChanged",
}

# WPF / framework properties that legitimately appear in binding paths (RelativeSource/ElementName/TemplatedParent
# bindings, or collection helpers).
WPF_PROPERTIES = {
    "Text", "IsChecked", "SelectedItem", "SelectedValue", "SelectedIndex", "DataContext", "ActualWidth", "ActualHeight",
    "IsMouseOver", "IsEnabled", "Visibility", "Count", "Content", "Tag", "Value", "Maximum", "Minimum", "Width", "Height",
    "IsKeyboardFocused", "IsSelected", "Items", "HasItems", "IsCompactSidebar", "Foreground", "Background", "IsDropDownOpen",
    "VerticalOffset",
}


class Problems:
    def __init__(self) -> None:
        self.items: list[str] = []

    def add(self, file: Path, message: str) -> None:
        self.items.append(f"{file.relative_to(ROOT)}: {message}")


def keys_in_order(tree: ET.ElementTree) -> list[str]:
    return [el.attrib[f"{X}Key"] for el in tree.iter() if f"{X}Key" in el.attrib]


def markup_refs(text: str, kind: str) -> list[str]:
    return re.findall(r"\{" + kind + r"\s+([^}\s,]+)\s*\}", text)


def runtime_brush_keys() -> set[str]:
    """Keys ThemeService puts into Application.Resources at startup."""
    palette = (APP / "Themes" / "ThemePalette.cs").read_text(encoding="utf-8")
    service = (APP / "Themes" / "ThemeService.cs").read_text(encoding="utf-8")
    keys: set[str] = set()
    for key in re.findall(r'\["([A-Za-z.]+)"\]\s*=', palette):
        keys.add(key + ".Brush" if key.startswith("Series.") else "Brush." + key)
    keys.update(re.findall(r'"((?:Brush|Series)\.[A-Za-z.]+)"', service))
    keys.update(re.findall(r'_resources\["([A-Za-z.]+)"\]', service))
    surface = re.search(r"SurfaceKeys\s*=\s*\[([^\]]*)\]", service)
    if surface:
        keys.update("Brush." + k for k in re.findall(r'"(\w+)"', surface.group(1)))
    return keys


def public_properties() -> set[str]:
    names: set[str] = set()
    sources = list((ROOT / "src" / "Coreloom.ViewModels").rglob("*.cs")) + list((ROOT / "src" / "Coreloom.Core").rglob("*.cs"))
    for path in sources:
        text = path.read_text(encoding="utf-8")
        names.update(re.findall(r"public\s+(?:required\s+|override\s+|virtual\s+|static\s+)*[\w<>\[\]?,. ()]+?\s+(\w+)\s*(?:\{|=>)", text))
        # positional record parameters become properties
        for params in re.findall(r"record(?:\s+struct|\s+class)?\s+\w+(?:<[^>]+>)?\s*\(([^)]*)\)", text):
            names.update(re.findall(r"(\w+)\s*(?:,|$|=)", params.strip()))
    return names


def property_access() -> dict[str, dict[str, bool]]:
    """class name -> {property name: has a public setter} for the view models and core models."""
    classes: dict[str, dict[str, bool]] = {}
    sources = list((ROOT / "src" / "Coreloom.ViewModels").rglob("*.cs")) + list((ROOT / "src" / "Coreloom.Core").rglob("*.cs"))
    decl = re.compile(r"public\s+(?:required\s+|override\s+|virtual\s+|new\s+)*[\w<>\[\]?,. ()]+?\s+(\w+)\s*(\{|=>)")
    class_decl = re.compile(r"\b(?:class|record|struct)\s+(\w+)")
    for path in sources:
        text = path.read_text(encoding="utf-8")
        class_positions = [(m.start(), m.group(1)) for m in class_decl.finditer(text)]
        for m in decl.finditer(text):
            name, opener = m.group(1), m.group(2)
            owner = next((c for pos, c in reversed(class_positions) if pos < m.start()), None)
            if owner is None or name == owner:
                continue
            if opener == "=>":
                classes.setdefault(owner, {})[name] = False
                continue
            depth, i = 0, m.end() - 1
            while i < len(text):
                if text[i] == "{":
                    depth += 1
                elif text[i] == "}":
                    depth -= 1
                    if depth == 0:
                        break
                i += 1
            block = text[m.end():i]
            top = re.sub(r"\{[^{}]*\}", "", re.sub(r"\{[^{}]*\}", "", block))
            if "get" not in top and "set" not in top:
                continue  # a method or a type, not a property
            classes.setdefault(owner, {})[name] = bool(
                re.search(r"(?<![\w.])set\b", top) and not re.search(r"(private|protected|internal)\s+set\b", top))
        for owner, params in re.findall(r"record(?:\s+struct|\s+class)?\s+(\w+)(?:<[^>]+>)?\s*\(([^)]*)\)", text):
            for name in re.findall(r"(\w+)\s*(?:,|$|=)", params.strip()):
                classes.setdefault(owner, {})[name] = False
    return classes


def property_types() -> dict[tuple[str, str], str]:
    types: dict[tuple[str, str], str] = {}
    sources = list((ROOT / "src" / "Coreloom.ViewModels").rglob("*.cs")) + list((ROOT / "src" / "Coreloom.Core").rglob("*.cs"))
    class_decl = re.compile(r"\b(?:class|record|struct)\s+(\w+)")
    decl = re.compile(r"public\s+(?:required\s+|override\s+|virtual\s+)*([\w<>\[\]?.]+)\s+(\w+)\s*(?:\{|=>)")
    for path in sources:
        text = path.read_text(encoding="utf-8")
        class_positions = [(m.start(), m.group(1)) for m in class_decl.finditer(text)]
        for m in decl.finditer(text):
            owner = next((c for pos, c in reversed(class_positions) if pos < m.start()), None)
            if owner:
                types[(owner, m.group(2))] = m.group(1).rstrip("?").split(".")[-1]
    return types


PROPERTY_TYPES: dict[tuple[str, str], str] = {}

# Dependency properties that bind TwoWay unless Mode says otherwise (element local name, attribute).
TWO_WAY_DEFAULT = {
    ("ProgressBar", "Value"), ("Slider", "Value"), ("ScrollBar", "Value"),
    ("CheckBox", "IsChecked"), ("ToggleButton", "IsChecked"), ("RadioButton", "IsChecked"), ("MenuItem", "IsChecked"),
    ("ComboBox", "SelectedItem"), ("ComboBox", "SelectedValue"), ("ComboBox", "SelectedIndex"), ("ComboBox", "Text"),
    ("ComboBox", "IsDropDownOpen"),
    ("ListBox", "SelectedItem"), ("ListBox", "SelectedValue"), ("ListBox", "SelectedIndex"),
    ("TextBox", "Text"), ("Run", "Text"), ("Expander", "IsExpanded"), ("Popup", "IsOpen"), ("ListBoxItem", "IsSelected"),
}


def binding_paths(text: str) -> list[str]:
    paths: list[str] = []
    for match in re.finditer(r"\{Binding(?:\s+([^,}=]+?))?(\s*,[^}]*)?\}", text):
        path = (match.group(1) or "").strip()
        rest = match.group(2) or ""
        explicit = re.search(r"Path=([^,}\s]+)", rest)
        if explicit:
            path = explicit.group(1)
        if path and not path.startswith("("):
            paths.append(path)
    # <Binding Path="X" /> elements inside MultiBinding
    paths.extend(re.findall(r'<Binding\s+Path="([^"]+)"', text))
    return paths


def main() -> int:
    problems = Problems()
    runtime_keys = runtime_brush_keys()
    PROPERTY_TYPES.update(property_types())
    props = public_properties()

    trees: dict[Path, ET.ElementTree] = {}
    for path in [APP / "App.xaml", *(APP / p for p in GLOBAL_ORDER), *VIEWS]:
        try:
            trees[path] = ET.parse(path)
        except ET.ParseError as exc:
            problems.add(path, f"XML parse error: {exc}")
    if problems.items:
        print("\n".join(problems.items))
        return 1

    # ---- resource keys -------------------------------------------------------------------------
    defined_so_far: set[str] = set()
    for rel in GLOBAL_ORDER:
        path = APP / rel
        text = path.read_text(encoding="utf-8")
        local_keys = keys_in_order(trees[path])
        for key in markup_refs(text, "StaticResource"):
            if key in runtime_keys or key.startswith(("Brush.", "Series.")):
                problems.add(path, f"runtime brush '{key}' must be a DynamicResource")
            elif key not in defined_so_far and key not in local_keys:
                problems.add(path, f"StaticResource '{key}' is not defined in this or an earlier dictionary")
            elif key in local_keys and key not in defined_so_far:
                # must be defined earlier in the same file (StaticResource resolves while loading)
                use = text.find("{StaticResource " + key)
                define = text.find(f'x:Key="{key}"')
                if define > use:
                    problems.add(path, f"StaticResource '{key}' is used before it is defined")
        defined_so_far.update(local_keys)

    global_keys = set(defined_so_far)
    all_keys = global_keys | runtime_keys

    for path in [APP / "App.xaml", *(APP / p for p in GLOBAL_ORDER), *VIEWS]:
        text = path.read_text(encoding="utf-8")
        local_keys = set(keys_in_order(trees[path]))
        if path in VIEWS or path.name == "App.xaml":
            for key in markup_refs(text, "StaticResource"):
                if key.startswith(("Brush.", "Series.")):
                    problems.add(path, f"runtime brush '{key}' must be a DynamicResource")
                elif key not in global_keys and key not in local_keys:
                    problems.add(path, f"StaticResource '{key}' is not defined")
        for key in markup_refs(text, "DynamicResource"):
            if key not in all_keys and key not in local_keys:
                problems.add(path, f"DynamicResource '{key}' is not defined anywhere (and not created by ThemeService)")

    # ---- a property set twice -------------------------------------------------------------------
    # An attribute and a property element for the same property (Style="…" and <TextBlock.Style>) compiles in the
    # XAML checker here but fails the real XAML compiler ("property has already been set and can be set only once").
    for path, tree in trees.items():
        for element in tree.iter():
            attributes = {name.split("}")[-1] for name in element.attrib}
            for child in element:
                child_tag = child.tag.split("}")[-1]
                if "." in child_tag and child_tag.split(".", 1)[1] in attributes:
                    problems.add(path, f"{child_tag} is set both as an attribute and as a property element")

    # ---- array-typed dependency properties ------------------------------------------------------
    # The XAML compiler rejects array-typed properties inside DataTemplate/ControlTemplate sections ("Tags of type
    # 'PropertyArrayStart' are not supported in template sections"), even when the value is a {Binding}. Custom
    # controls therefore expose IReadOnlyList<T> (an array still binds to it).
    for cs_path in sorted(APP.rglob("*.cs")):
        if "/obj/" in str(cs_path) or "/bin/" in str(cs_path):
            continue
        cs_text = cs_path.read_text(encoding="utf-8")
        for match in re.finditer(r"DependencyProperty\.Register(?:Attached)?\s*\(\s*(?:nameof\((\w+)\)|\"(\w+)\")\s*,\s*typeof\(([^()]*?\[\])\)", cs_text):
            name = match.group(1) or match.group(2)
            problems.add(cs_path, f"dependency property '{name}' is array-typed ({match.group(3)}); use IReadOnlyList<T> so it can be set inside templates")

    # ---- windows on their own UI thread --------------------------------------------------------
    for path in VIEWS:
        if path.name not in SELF_CONTAINED_VIEWS:
            continue
        text = path.read_text(encoding="utf-8")
        local_keys = set(keys_in_order(trees[path]))
        for kind in ("StaticResource", "DynamicResource"):
            for key in markup_refs(text, kind):
                if key not in local_keys:
                    problems.add(path, f"{kind} '{key}' would come from App.xaml, but this window runs on its own UI thread")
        behind = path.with_name(path.name + ".cs")
        if not behind.exists() or "InheritanceBehavior.SkipToThemeNext" not in behind.read_text(encoding="utf-8"):
            problems.add(path, "code-behind must set InheritanceBehavior = InheritanceBehavior.SkipToThemeNext (keeps lookups off App.xaml)")

    # ---- x:Class, handlers, names ------------------------------------------------------------
    code = {p: p.read_text(encoding="utf-8") for p in APP.rglob("*.cs") if "/obj/" not in str(p) and "/bin/" not in str(p)}
    all_code = "\n".join(code.values())
    for path in [APP / "App.xaml", *VIEWS]:
        root = trees[path].getroot()
        cls = root.attrib.get(f"{X}Class")
        if not cls:
            problems.add(path, "missing x:Class")
            continue
        short = cls.split(".")[-1]
        if not re.search(rf"partial\s+class\s+{short}\b", all_code):
            problems.add(path, f"no partial class {short} in code-behind")
            continue
        body_files = [t for t in code.values() if re.search(rf"partial\s+class\s+{short}\b", t)]
        body = "\n".join(body_files)
        names = {el.attrib[f"{X}Name"] for el in root.iter() if f"{X}Name" in el.attrib}
        for el in root.iter():
            for attr, value in el.attrib.items():
                local = attr.split("}")[-1]
                if local in EVENT_ATTRS and not value.startswith("{"):
                    if not re.search(rf"\b(void|Task)\s+{re.escape(value)}\s*\(", body):
                        problems.add(path, f"handler '{value}' for {local} not found in {short}")
        text = path.read_text(encoding="utf-8")
        for name in re.findall(r"ElementName=(\w+)", text):
            if name not in names:
                problems.add(path, f"ElementName '{name}' does not exist")
        # fields used from code-behind that should come from x:Name
        for field in set(re.findall(r"\b([A-Z]\w+)\.(?:SelectedItem|ItemsSource|Content|ToolTip|Margin|Width|BeginAnimation)\b", body)):
            if field not in names and not re.search(rf"\b(class|var|static)\s+{field}\b", all_code) and field not in {"Motion", "AnimatedText", "Timeline", "Sidebar"}:
                if field[0].isupper() and field not in ("ThemeService", "NativeWindow", "SnapLayoutSupport", "WindowState"):
                    problems.add(path, f"code-behind uses '{field}' which is not an x:Name in the XAML")
        if short == "MainWindow":
            for required in ["Sidebar", "NavList", "SettingsNav", "MaximizeButton", "RootBorder", "LivePulse", "LivePulseScale"]:
                if required not in names:
                    problems.add(path, f"code-behind expects x:Name '{required}'")

    # ---- bindings ----------------------------------------------------------------------------------
    for path in [*(APP / p for p in GLOBAL_ORDER), *VIEWS]:
        text = path.read_text(encoding="utf-8")
        for binding in binding_paths(text):
            for segment in re.split(r"[.\[\]]", binding):
                segment = segment.strip()
                if not segment or segment.isdigit():
                    continue
                if segment not in props and segment not in WPF_PROPERTIES:
                    problems.add(path, f"binding path segment '{segment}' (in '{binding}') is not a known property")

    # ---- TwoWay-by-default bindings must target writable properties --------------------------------
    access = property_access()
    page_models = {"OverviewPage": "OverviewViewModel", "GpuPage": "GpuViewModel", "CpuPage": "CpuViewModel",
                   "MemoryPage": "MemoryViewModel", "StoragePage": "StorageViewModel", "NetworkPage": "NetworkViewModel",
                   "HistoryPage": "HistoryViewModel", "SettingsPage": "SettingsViewModel", "MainWindow": "ShellViewModel"}

    def resolve(owner: str | None, dotted: str) -> tuple[str | None, str]:
        """Walk a dotted path through known property types (best effort)."""
        parts = dotted.split(".")
        for part in parts[:-1]:
            owner = PROPERTY_TYPES.get((owner, part)) if owner else None
        return owner, parts[-1]

    for path in [*(APP / p for p in GLOBAL_ORDER), *VIEWS]:
        root = trees[path].getroot()
        parents = {child: parent for parent in root.iter() for child in parent}
        default_owner = page_models.get(path.stem)
        for el in root.iter():
            tag = el.tag.split("}")[-1]
            for attr, value in el.attrib.items():
                local = attr.split("}")[-1]
                if (tag, local) not in TWO_WAY_DEFAULT or not value.startswith("{Binding"):
                    continue
                if re.search(r"Mode=(OneWay|OneTime)", value) or "RelativeSource" in value or "ElementName" in value:
                    continue
                inner = value[len("{Binding"):].strip().rstrip("}")
                first = inner.split(",")[0].strip()
                explicit = re.search(r"Path=([\w.]+)", inner)
                bound = explicit.group(1) if explicit else ("" if "=" in first else first)
                if not bound:
                    continue
                # Context: nearest DataTemplate/DataContext type, else the page's view model.
                owner, node = default_owner, el
                while node in parents:
                    node = parents[node]
                    dtype = node.attrib.get("DataType", "")
                    match = re.search(r"\{x:Type\s+\w+:(\w+)\}", dtype)
                    if node.tag.endswith("DataTemplate") and match:
                        owner = match.group(1)
                        break
                    context = node.attrib.get("DataContext", "")
                    ctx_match = re.match(r"\{Binding\s+([\w.]+)\}", context)
                    if ctx_match:
                        owner = resolve(owner, ctx_match.group(1) + ".x")[0]
                        break
                cls, prop = resolve(owner, bound)
                known = access.get(cls or "", {})
                if prop in known and not known[prop]:
                    problems.add(path, f"{tag}.{local} binds TwoWay by default but {cls}.{prop} is read-only; add Mode=OneWay")
                elif cls is None or prop not in known:
                    # Unknown context: be strict only when every class declaring the name has it read-only.
                    declared = [props[prop] for props in access.values() if prop in props]
                    if declared and not any(declared):
                        problems.add(path, f"{tag}.{local} binds TwoWay by default but '{bound}' is read-only; add Mode=OneWay")

    # ---- icon keys referenced from code / view models -------------------------------------------
    vm_text = "\n".join(p.read_text(encoding="utf-8") for p in (ROOT / "src" / "Coreloom.ViewModels").rglob("*.cs"))
    for key in set(re.findall(r'"(Icon\.\w+)"', vm_text + all_code)):
        if key not in global_keys:
            problems.add(APP / "Themes" / "Icons.xaml", f"icon '{key}' is referenced from code but not defined")
    for key in set(re.findall(r'FindResource\("([^"]+)"', all_code)) | set(re.findall(r'"(Glyph\.\w+)"', all_code)):
        if key not in all_keys:  # theme brushes are added at runtime
            problems.add(APP / "Views" / "MainWindow.xaml.cs", f"resource '{key}' looked up from code is not defined")

    # ---- pack URIs -------------------------------------------------------------------------------
    csproj = (APP / "Coreloom.App.csproj").read_text(encoding="utf-8")
    everything = all_code + "\n".join(p.read_text(encoding="utf-8") for p in trees)
    # <Resource Include="source" [Link="published path"] />: the pack URI path is the Link when there is one.
    resources: list[tuple[str, str]] = []
    for m in re.finditer(r'<Resource\s+Include="([^"]+)"(?:\s+Link="([^"]+)")?', csproj):
        # Wildcard includes publish under a Link built from item metadata ("Assets\PC\%(Filename)%(Extension)").
        # A link of "%(Filename)%(Extension)" keeps the source's own file name pattern ("coreloom-dark-*.png").
        link = (m.group(2) or "").replace("%(Filename)%(Extension)", m.group(1).replace("/", chr(92)).rsplit(chr(92), 1)[-1])
        link = re.sub(r"%\((RecursiveDir|Filename|Extension)\)", "*", link) or m.group(1)
        resources.append((re.sub(r"\*+", "*", link), m.group(1)))

    def published(rel: str):
        """The (published pattern, source pattern) entry that embeds this pack path, or None."""
        win = rel.replace("/", chr(92))
        return next(((pub, src) for pub, src in resources if fnmatch.fnmatch(win, pub)), None)

    xaml_text = "\n".join(p.read_text(encoding="utf-8") for p in trees)

    def source_exists(rel: str, entry) -> bool:
        pub, src = entry
        if "*" in src:
            # A wildcard include: files referenced from XAML must exist (code may probe for optional ones, such as a
            # vendor logo or pc.png, before using them).
            if f"pack://application:,,,/{rel}" not in xaml_text:
                return True
            folder = src.replace(chr(92), "/").rsplit("/", 1)[0]
            return (APP / folder / rel.rsplit("/", 1)[-1]).resolve().exists()
        return (APP / src.replace(chr(92), "/")).resolve().exists()

    for uri in set(re.findall(r"pack://application:,,,/([\w/.-]+?)(?=[\s\"'#,)]|$)", everything)):
        uri = uri.rstrip("/")
        file = APP / uri
        if file.is_dir():
            # A font family reference ("/Fonts/#Inter"): every .ttf in the folder must be a Resource.
            for ttf in file.glob("*.ttf"):
                if not published(f"{uri}/{ttf.name}"):
                    problems.add(APP / "Coreloom.App.csproj", f"'{uri}/{ttf.name}' is not included as <Resource>")
            continue
        entry = published(uri)
        if entry is None:
            problems.add(APP / "Coreloom.App.csproj", f"'{uri}' is not included as <Resource>")
        elif not source_exists(uri, entry):
            problems.add(APP / "Coreloom.App.csproj", f"pack URI target '{uri}' is a <Resource> whose source file '{entry[1]}' does not exist")

    # Embedded font families referenced from XAML/C# must exist in Fonts/ (family name from the file's name table).
    try:
        from fontTools.ttLib import TTFont  # type: ignore

        families: set[str] = set()
        for ttf in (APP / "Fonts").glob("*.ttf"):
            names = TTFont(str(ttf))["name"]
            rec = names.getName(16, 3, 1, 0x409) or names.getName(1, 3, 1, 0x409)
            if rec:
                families.add(rec.toUnicode())
        for family in set(re.findall(r"Fonts/#([\w ]+?)(?=[\"'<,]|$)", everything)):
            if family not in families:
                problems.add(APP / "Fonts", f"font family '{family}' is referenced but no embedded font declares it (have: {sorted(families)})")
    except ImportError:
        pass

    # ---- settings sections -----------------------------------------------------------------------
    settings_vm = (ROOT / "src" / "Coreloom.ViewModels" / "Pages" / "SettingsViewModel.cs").read_text(encoding="utf-8")
    sections = re.search(r"Sections\s*\{\s*get;\s*\}\s*=\s*\[([^\]]*)\]", settings_vm)
    page = (APP / "Views" / "SettingsPage.xaml").read_text(encoding="utf-8")
    if sections:
        declared = re.findall(r'"(\w+)"', sections.group(1))
        shown = re.findall(r"ConverterParameter=(\w+)\}\"\s*>", page) + re.findall(r"EqualsToVisibility\},\s*ConverterParameter=(\w+)", page)
        for name in declared:
            if name not in shown:
                problems.add(APP / "Views" / "SettingsPage.xaml", f"settings section '{name}' has no panel")

    if problems.items:
        print("\n".join(sorted(set(problems.items))))
        print(f"\n{len(set(problems.items))} problem(s)")
        return 1

    n_bindings = sum(len(binding_paths(p.read_text(encoding='utf-8'))) for p in [*(APP / q for q in GLOBAL_ORDER), *VIEWS])
    print(f"XAML checks passed: {len(trees)} files, {len(all_keys)} resource keys, {n_bindings} bindings checked.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
