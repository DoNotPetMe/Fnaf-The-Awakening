#!/usr/bin/env python3
"""
Structural validation for the The Awakening Unity project.

There is no C# compiler in CI here, so this does what can be checked without one:
balanced delimiters in every source file (with a real C#-aware scanner that
understands comments, strings, verbatim strings, interpolation and char literals),
JSON validity, assembly-definition reference resolution, Unity YAML sanity, and
project layout. It catches the class of mistake that costs a round trip through
the Unity editor.

Exit code 0 = clean, 1 = problems found.
"""

from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

RED = "\033[31m"
YEL = "\033[33m"
GRN = "\033[32m"
DIM = "\033[2m"
END = "\033[0m"

errors: list[str] = []
warnings: list[str] = []


def err(msg: str) -> None:
    errors.append(msg)


def warn(msg: str) -> None:
    warnings.append(msg)


# ---------------------------------------------------------------------------
# C# scanning
# ---------------------------------------------------------------------------

def strip_csharp(src: str) -> str:
    """Replace comments and literal text with spaces, preserving line structure.

    Implemented as a small context stack rather than a flat loop, because C#
    interpolated strings nest: $"{(flag ? "a" : "b")}" contains a *string inside a
    string*. A flat scanner ends the outer literal at the first inner quote and then
    reports phantom unbalanced braces for the rest of the file. Contexts are:

      code  -> tracks brace depth so an interpolation hole knows when it closes
      str   -> a literal, flagged verbatim (@) and/or interpolated ($)

    Braces belonging to an interpolation hole are emitted as a matched pair, so
    overall balance is unaffected; everything else inside a literal becomes space.
    """
    out: list[str] = []
    i = 0
    n = len(src)
    # Each frame: ["code", brace_depth] or ["str", interpolated, verbatim]
    stack: list[list] = [["code", 0]]

    def emit(text: str) -> None:
        out.append(text)

    def blank(text: str) -> None:
        out.append("".join("\n" if ch == "\n" else " " for ch in text))

    while i < n:
        top = stack[-1]

        # ------------------------------------------------------------------ code
        if top[0] == "code":
            c = src[i]
            nxt = src[i + 1] if i + 1 < n else ""

            if c == "/" and nxt == "/":
                j = src.find("\n", i)
                j = n if j < 0 else j
                blank(src[i:j])
                i = j
                continue

            if c == "/" and nxt == "*":
                j = src.find("*/", i + 2)
                j = n if j < 0 else j + 2
                blank(src[i:j])
                i = j
                continue

            # literal openers, longest prefix first
            for prefix, interp, verbatim in (
                ('$@"', True, True),
                ('@$"', True, True),
                ('@"', False, True),
                ('$"', True, False),
                ('"', False, False),
            ):
                if src.startswith(prefix, i):
                    blank(prefix)
                    i += len(prefix)
                    stack.append(["str", interp, verbatim])
                    break
            else:
                if c == "'":
                    j = i + 1
                    while j < n and src[j] != "'":
                        j += 2 if src[j] == "\\" else 1
                    j = min(j + 1, n)
                    blank(src[i:j])
                    i = j
                    continue

                if c == "{":
                    top[1] += 1
                    emit("{")
                    i += 1
                    continue

                if c == "}":
                    if top[1] > 0:
                        top[1] -= 1
                        emit("}")
                    elif len(stack) > 1:
                        # closes an interpolation hole; hand control back to the literal
                        stack.pop()
                        emit("}")
                    else:
                        emit("}")          # stray; check_balance reports it
                    i += 1
                    continue

                emit(c)
                i += 1
            continue

        # --------------------------------------------------------------- literal
        _, interpolated, verbatim = top
        c = src[i]

        if not verbatim and c == "\\":
            blank(src[i:i + 2])
            i += 2
            continue

        if c == '"':
            if verbatim and src[i + 1:i + 2] == '"':
                blank('""')
                i += 2
                continue
            blank('"')
            i += 1
            stack.pop()
            continue

        if interpolated and c == "{":
            if src[i + 1:i + 2] == "{":
                blank("{{")
                i += 2
                continue
            emit("{")
            i += 1
            stack.append(["code", 0])
            continue

        if interpolated and c == "}" and src[i + 1:i + 2] == "}":
            blank("}}")
            i += 2
            continue

        if not verbatim and c == "\n":
            # unterminated literal — bail out of it so the rest still parses
            stack.pop()
            emit("\n")
            i += 1
            continue

        blank(c)
        i += 1

    return "".join(out)


def check_balance(path: Path, code: str) -> None:
    pairs = {"}": "{", ")": "(", "]": "["}
    opens = {"{": "}", "(": ")", "[": "]"}
    stack: list[tuple[str, int]] = []
    line = 1
    for ch in code:
        if ch == "\n":
            line += 1
        elif ch in opens:
            stack.append((ch, line))
        elif ch in pairs:
            if not stack:
                err(f"{rel(path)}:{line}: stray closing '{ch}'")
                return
            op, ol = stack.pop()
            if op != pairs[ch]:
                err(f"{rel(path)}:{line}: '{ch}' closes '{op}' opened at line {ol}")
                return
    if stack:
        op, ol = stack[-1]
        err(f"{rel(path)}:{ol}: '{op}' is never closed")


def rel(p: Path) -> str:
    try:
        return str(p.relative_to(ROOT))
    except ValueError:
        return str(p)


# ---------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------

def check_csharp() -> int:
    count = 0
    for path in sorted(ROOT.rglob("*.cs")):
        if ".git" in path.parts or "Library" in path.parts:
            continue
        count += 1
        try:
            src = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            err(f"{rel(path)}: not valid UTF-8")
            continue

        code = strip_csharp(src)
        check_balance(path, code)

        if "namespace " not in code:
            warn(f"{rel(path)}: no namespace declaration")

        if "\t" in src:
            warn(f"{rel(path)}: contains tab characters (project uses 4 spaces)")

        # A very common paste error: two consecutive semicolons outside a for-header.
        for m in re.finditer(r";;", code):
            ln = code[: m.start()].count("\n") + 1
            if "for" not in code[max(0, m.start() - 120) : m.start()]:
                warn(f"{rel(path)}:{ln}: doubled semicolon")

        # UnityEngine.Debug used directly instead of GLog in gameplay code.
        exempt = path.name in ("GLog.cs", "ConsoleLogCapture.cs")
        if not exempt and "/DevTools/" not in str(path) and "/Editor/" not in str(path) \
           and "/Tests/" not in str(path) and re.search(r"(?<![.\w])Debug\.Log", code):
            warn(f"{rel(path)}: uses Debug.Log directly; prefer GLog for channel filtering")
    return count


def check_json() -> int:
    count = 0
    for pattern in ("*.json", "*.asmdef", "*.asmref"):
        for path in sorted(ROOT.rglob(pattern)):
            if ".git" in path.parts or "Library" in path.parts:
                continue
            count += 1
            try:
                json.loads(path.read_text(encoding="utf-8"))
            except Exception as ex:                      # noqa: BLE001
                err(f"{rel(path)}: invalid JSON — {ex}")
    return count


def check_asmdefs() -> None:
    defined: dict[str, Path] = {}
    for path in ROOT.rglob("*.asmdef"):
        if ".git" in path.parts:
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except Exception:                                # noqa: BLE001
            continue
        name = data.get("name")
        if not name:
            err(f"{rel(path)}: asmdef has no name")
            continue
        if name in defined:
            err(f"{rel(path)}: duplicate assembly name '{name}' (also {rel(defined[name])})")
        defined[name] = path

    # Grotto.* references must resolve locally; package references are taken on trust.
    edges: dict[str, list[str]] = {}
    for name, path in defined.items():
        data = json.loads(path.read_text(encoding="utf-8"))
        refs = [r for r in data.get("references", []) if isinstance(r, str)]
        local = [r for r in refs if r.startswith("Grotto.")]
        for r in local:
            if r not in defined:
                err(f"{rel(path)}: references unknown assembly '{r}'")
        edges[name] = local

    # Cycle detection — Unity rejects circular assembly references outright.
    WHITE, GREY, BLACK = 0, 1, 2
    colour = {k: WHITE for k in edges}

    def visit(node: str, trail: list[str]) -> None:
        colour[node] = GREY
        for nb in edges.get(node, []):
            if nb not in colour:
                continue
            if colour[nb] == GREY:
                err("circular assembly reference: " + " -> ".join(trail + [node, nb]))
            elif colour[nb] == WHITE:
                visit(nb, trail + [node])
        colour[node] = BLACK

    for node in list(colour):
        if colour[node] == WHITE:
            visit(node, [])

    # Every script folder under Assets/Game/Scripts should be covered by an asmdef.
    scripts = ROOT / "Assets" / "Game" / "Scripts"
    if scripts.is_dir():
        for child in sorted(scripts.iterdir()):
            if not child.is_dir():
                continue
            has_cs = any(child.rglob("*.cs"))
            has_def = any(child.glob("*.asmdef")) or any(
                p.glob("*.asmdef") for p in [child, *child.parents]
            )
            if has_cs and not any(child.glob("*.asmdef")) and not has_def:
                warn(f"Assets/Game/Scripts/{child.name}: scripts with no assembly definition")


def check_cross_assembly_usage() -> None:
    """Verify every cross-assembly reference is declared in the asmdef.

    This is the single most valuable check here, because it catches a genuine
    compile error that nothing else can see without a compiler: a file that
    `using`s another Grotto namespace whose assembly its own asmdef does not
    reference. Unity reports it as a confusing "type or namespace not found".
    """
    # folder -> (assembly name, set of Grotto.* references)
    owners: dict[Path, tuple[str, set[str]]] = {}
    for path in ROOT.rglob("*.asmdef"):
        if ".git" in path.parts:
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except Exception:                                # noqa: BLE001
            continue
        name = data.get("name")
        if not name:
            continue
        refs = {r for r in data.get("references", [])
                if isinstance(r, str) and r.startswith("Grotto.")}
        owners[path.parent] = (name, refs)

    def owning(file: Path) -> Path | None:
        """Deepest asmdef folder containing this file — Unity's own rule."""
        best = None
        for folder in owners:
            try:
                file.relative_to(folder)
            except ValueError:
                continue
            if best is None or len(folder.parts) > len(best.parts):
                best = folder
        return best

    known = "|".join(sorted({n.split(".")[1] for n, _ in owners.values() if "." in n}))
    if not known:
        return

    using_re = re.compile(r"^using +(Grotto\.[A-Za-z.]+) *;", re.M)
    qualified_re = re.compile(r"\bGrotto\.(" + known + r")\.")

    for path in sorted(ROOT.rglob("*.cs")):
        if ".git" in path.parts or "Library" in path.parts:
            continue

        folder = owning(path)
        if folder is None:
            warn(f"{rel(path)}: not covered by any assembly definition")
            continue

        name, refs = owners[folder]
        src = path.read_text(encoding="utf-8")
        code = strip_csharp(src)
        reported: set[str] = set()

        for match in using_re.finditer(code):
            namespace = match.group(1)
            # Grotto.AI.Behaviours lives in the Grotto.AI assembly.
            assembly = ".".join(namespace.split(".")[:2])
            if assembly == name or assembly in refs or assembly in reported:
                continue
            reported.add(assembly)
            err(f"{rel(path)}: uses '{namespace}' but assembly '{name}' "
                f"does not reference '{assembly}'")

        for match in qualified_re.finditer(code):
            assembly = "Grotto." + match.group(1)
            if assembly == name or assembly in refs or assembly in reported:
                continue
            reported.add(assembly)
            line = code[: match.start()].count("\n") + 1
            err(f"{rel(path)}:{line}: fully-qualified '{assembly}' but assembly "
                f"'{name}' does not reference it")


# Types that live in a namespace you have to import, and the using that provides
# them. Only types common enough that a missing using is a real, recurring mistake —
# a longer list would trade false positives for coverage nobody needs.
NAMESPACE_TYPES: dict[str, tuple[str, ...]] = {
    "System.Collections.Generic": (
        "List", "Dictionary", "HashSet", "Queue", "Stack", "SortedList",
        "SortedDictionary", "LinkedList", "IReadOnlyList", "IReadOnlyDictionary",
        "IReadOnlyCollection", "IList", "IDictionary", "ISet", "IEnumerable",
        "KeyValuePair", "Comparer", "EqualityComparer",
    ),
    "System": (
        "Action", "Func", "Exception", "IDisposable", "IEquatable", "IComparable",
        "StringComparison", "DateTime", "TimeSpan", "Convert", "Math",
    ),
    "System.Text": ("StringBuilder",),
    "System.IO": ("File", "Directory", "Path", "StreamReader", "StreamWriter"),
    "System.Linq": (),   # method-based; not detectable this way
}


def check_missing_usings() -> None:
    """Catch a type used without the `using` that declares it.

    This is CS0246, and it is the single most common compile error in a project
    written without a compiler to hand. Unity reports it as "the type or namespace
    name 'IReadOnlyList<>' could not be found", which is clear enough once you see
    it — but you only see it after a full import, and only for the first assembly
    that fails. Everything behind it stays hidden.

    The check is deliberately conservative. A type only counts when it is used in a
    position that cannot be anything else: `IReadOnlyList<`, `new List<`, `List<int>
    name`. A bare identifier is ignored, because `Action` might be a local variable
    and a false positive in a pre-commit check is worse than a miss.

    Fully-qualified uses (`System.Collections.Generic.List<...>`) are fine and are
    not reported, and neither is a type the file defines itself.
    """
    for path in sorted(ROOT.rglob("*.cs")):
        if ".git" in path.parts:
            continue

        raw = path.read_text(encoding="utf-8", errors="replace")
        code = strip_csharp(raw)

        usings = set(re.findall(r"^\s*using\s+(?:static\s+)?([\w.]+)\s*;", raw, re.M))

        # `using X = Y;` aliases and anything the file declares itself.
        declared = set(re.findall(r"\b(?:class|struct|interface|enum|record)\s+(\w+)", code))
        declared |= set(re.findall(r"^\s*using\s+(\w+)\s*=", raw, re.M))

        for namespace, types in NAMESPACE_TYPES.items():
            if namespace in usings:
                continue

            # A parent namespace does not import a child's types in C#, but being
            # inside `namespace System.Something` does bring System into scope.
            file_ns = re.search(r"^\s*namespace\s+([\w.]+)", code, re.M)
            if file_ns and (file_ns.group(1) == namespace
                            or file_ns.group(1).startswith(namespace + ".")):
                continue

            for type_name in types:
                if type_name in declared:
                    continue

                # Generic use, or a declaration/instantiation that can only be a type.
                pattern = (
                    rf"(?<![\w.]){type_name}\s*<"                      # List<...>
                    rf"|(?<![\w.])new\s+{type_name}\s*[<(]"           # new List(...)
                    rf"|(?<![\w.]){type_name}\.[A-Z]"                  # Path.Combine
                )

                match = re.search(pattern, code)
                if not match:
                    continue

                line = code[: match.start()].count("\n") + 1
                err(f"{path}:{line}: uses '{type_name}' but the file has no "
                    f"'using {namespace};' — this is CS0246 at compile time")
                break   # one report per namespace per file is enough to act on


def check_unity_type_filenames() -> None:
    """Every MonoBehaviour and ScriptableObject must live in a file of its own name.

    Unity resolves a serialised component or asset back to its script *by filename*,
    not by reflection. Put two MonoBehaviours in one file, or name the file anything
    but the class, and the type loads at compile time but the asset referencing it
    comes back as "the associated script cannot be loaded" — at runtime, on someone
    else's machine, long after the change that caused it.

    Nested types are exempt: they are addressed through their owner.
    """
    unity_bases = ("MonoBehaviour", "ScriptableObject", "EditorWindow", "Editor",
                   "ScriptableRendererFeature", "StateMachineBehaviour")

    for path in sorted(ROOT.rglob("*.cs")):
        if ".git" in path.parts:
            continue

        code = strip_csharp(path.read_text(encoding="utf-8", errors="replace"))
        stem = path.stem

        # Class declarations at namespace level — one leading indent at most, which
        # is what keeps nested types out of it.
        for match in re.finditer(
                r"^[ \t]{0,8}(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*"
                r"class\s+(\w+)\s*:\s*([\w<>,.\s]+?)[\r\n{]",
                code, re.M):

            name, bases = match.group(1), match.group(2)

            if not any(re.search(rf"(^|[\s,.]){b}\b", bases) for b in unity_bases):
                continue

            # A partial type may legitimately be split across `Name.Part.cs` files.
            if stem == name or stem.startswith(name + "."):
                continue

            line = code[: match.start()].count("\n") + 1
            err(f"{path}:{line}: '{name}' derives from a Unity type but the file is "
                f"'{stem}.cs'. Unity resolves these by filename — rename the file to "
                f"'{name}.cs' or move the class into its own.")


def check_shaders() -> int:
    count = 0
    for path in sorted(ROOT.rglob("*.shader")):
        if ".git" in path.parts:
            continue
        count += 1
        src = path.read_text(encoding="utf-8")

        # ShaderLab allows a leading comment block before the Shader declaration.
        head = "\n".join(
            line for line in src.splitlines()
            if line.strip() and not line.lstrip().startswith("//")
        )
        if not head.lstrip().startswith("Shader"):
            err(f"{rel(path)}: does not begin with a Shader declaration")
        if src.count("{") != src.count("}"):
            err(f"{rel(path)}: unbalanced braces ({src.count('{')} open, {src.count('}')} close)")
        m = re.search(r'Shader\s+"([^"]+)"', src)
        if m and not m.group(1).startswith("Grotto/"):
            warn(f"{rel(path)}: shader name '{m.group(1)}' is outside the Grotto/ namespace")
    return count


def check_layout() -> None:
    required = [
        "Packages/manifest.json",
        "ProjectSettings/ProjectVersion.txt",
        "ProjectSettings/TagManager.asset",
        "Assets/Game/Scripts/Core/Grotto.Core.asmdef",
        "README.md",
        "LICENSE",
        ".gitignore",
    ]
    for r in required:
        if not (ROOT / r).exists():
            err(f"missing required path: {r}")

    tm = ROOT / "ProjectSettings" / "TagManager.asset"
    if tm.exists():
        body = tm.read_text(encoding="utf-8")
        if "layers:" in body:
            seg = body.split("layers:")[1].split("m_SortingLayers:")[0]
            n = len([l for l in seg.splitlines() if l.startswith("  - ")])
            if n != 32:
                err(f"ProjectSettings/TagManager.asset: {n} layer entries, Unity requires exactly 32")


def main() -> int:
    cs = check_csharp()
    js = check_json()
    check_asmdefs()
    check_cross_assembly_usage()
    check_missing_usings()
    check_unity_type_filenames()
    sh = check_shaders()
    check_layout()

    print(f"{DIM}scanned {cs} C# files, {js} JSON files, {sh} shaders{END}")

    for w in warnings:
        print(f"{YEL}warn{END}  {w}")
    for e in errors:
        print(f"{RED}error{END} {e}")

    if errors:
        print(f"\n{RED}FAILED{END}: {len(errors)} error(s), {len(warnings)} warning(s)")
        return 1

    print(f"\n{GRN}OK{END}: 0 errors, {len(warnings)} warning(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
