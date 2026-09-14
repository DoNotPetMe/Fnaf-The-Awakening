#!/usr/bin/env python3
"""Structural checks on the authored site layouts.

There is no C# compiler in CI and no Unity licence, so a broken map would
otherwise only be found by opening the editor. The layouts are written through a
small, regular authoring API (``LayoutAuthoring.Node/Link/Place``), which makes
them cheap to parse and check directly from the source.

What this catches, in the order the failures actually happen when writing a map:

* a link naming a node that does not exist (a typo in an id);
* a node nothing can reach, which is a room the player will never see;
* a cast placement pointing at a room that is not on this map;
* an attack node that is not adjacent to the station, so the character can
  never reach a threshold and simply loiters all night;
* water gates in the wrong order, or with no dry band at all;
* an approach that no character uses, or a character with no way home.

Run: ``python3 Tools/check_layouts.py``
"""

from __future__ import annotations

import re
import sys
from dataclasses import dataclass, field
from pathlib import Path

SITES = Path("Assets/Game/Scripts/Facility/Sites")

# Traversal capabilities, matching TraversalMask.
WALK, CRAWL, CLIMB, SWIM, BURROW = 1, 2, 4, 8, 16
MASK_NAMES = {
    "Walk": WALK,
    "Crawl": CRAWL,
    "Climb": CLIMB,
    "Swim": SWIM,
    "Burrow": BURROW,
}


@dataclass
class Node:
    id: str
    name: str
    kind: str
    has_camera: bool


@dataclass
class Link:
    a: str
    b: str
    mask: int
    seconds: float
    min_water: float
    max_water: float
    barrier: str
    one_way: bool


@dataclass
class Place:
    who: str
    home: str
    retreat: str
    attacks: list


@dataclass
class Site:
    file: Path
    site_id: str = "?"
    name: str = "?"
    nodes: dict = field(default_factory=dict)
    links: list = field(default_factory=list)
    cast: list = field(default_factory=list)
    wiring: dict = field(default_factory=dict)
    gates: dict = field(default_factory=dict)
    consts: dict = field(default_factory=dict)


# ---------------------------------------------------------------------------
# Parsing
# ---------------------------------------------------------------------------

def strip_comments(text: str) -> str:
    """Removes // and /* */ comments without touching string literals."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if c == '"':
            out.append(c)
            i += 1
            while i < n:
                out.append(text[i])
                if text[i] == "\\":
                    i += 1
                    if i < n:
                        out.append(text[i])
                        i += 1
                    continue
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
            continue
        out.append(c)
        i += 1
    return "".join(out)


def split_args(body: str) -> list:
    """Splits a call's argument list on top-level commas."""
    args, depth, cur, in_string = [], 0, [], False
    i, n = 0, len(body)
    while i < n:
        c = body[i]
        if in_string:
            cur.append(c)
            if c == "\\":
                i += 1
                if i < n:
                    cur.append(body[i])
            elif c == '"':
                in_string = False
            i += 1
            continue
        if c == '"':
            in_string = True
            cur.append(c)
        elif c in "([{":
            depth += 1
            cur.append(c)
        elif c in ")]}":
            depth -= 1
            cur.append(c)
        elif c == "," and depth == 0:
            args.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
        i += 1
    if cur:
        args.append("".join(cur).strip())
    return [a for a in args if a]


def find_calls(text: str, name: str) -> list:
    """Every `name(...)` call in the text, returned as argument lists."""
    calls = []
    for match in re.finditer(rf"(?<![\w.]){re.escape(name)}\s*\(", text):
        i = match.end()
        depth, start, in_string = 1, i, False
        while i < len(text) and depth:
            c = text[i]
            if in_string:
                if c == "\\":
                    i += 1
                elif c == '"':
                    in_string = False
            elif c == '"':
                in_string = True
            elif c == "(":
                depth += 1
            elif c == ")":
                depth -= 1
            i += 1
        calls.append(split_args(text[start:i - 1]))
    return calls


def named(args: list) -> tuple:
    """Splits an argument list into positional values and named ones."""
    positional, keywords = [], {}
    for a in args:
        m = re.match(r"^([A-Za-z_]\w*)\s*:\s*(.*)$", a, re.S)
        # A ternary's colon is not a named argument; require no '?' before it.
        if m and "?" not in a.split(":", 1)[0]:
            keywords[m.group(1)] = m.group(2).strip()
        else:
            positional.append(a)
    return positional, keywords


def unquote(value: str) -> str:
    value = value.strip()
    if value.startswith('"') and value.endswith('"'):
        return value[1:-1]
    return value


def number(value: str, consts: dict) -> float:
    value = value.strip().rstrip("f")
    if value in consts:
        return consts[value]
    try:
        return float(value)
    except ValueError:
        return float("nan")


def mask_of(expr: str) -> int:
    mask = 0
    for part in re.findall(r"TraversalMask\.(\w+)", expr):
        mask |= MASK_NAMES.get(part, 0)
    return mask


def parse_site(path: Path) -> Site:
    raw = strip_comments(path.read_text(encoding="utf-8"))
    site = Site(file=path)

    for m in re.finditer(r"public const float (\w+)\s*=\s*([0-9.]+)f?;", raw):
        site.consts[m.group(1)] = float(m.group(2))

    strings = dict(re.findall(r'public const string (\w+)\s*=\s*"([^"]*)"', raw))

    m = re.search(r'layout\.siteId\s*=\s*(?:"([^"]*)"|(\w+))', raw)
    if m:
        site.site_id = m.group(1) if m.group(1) is not None else strings.get(m.group(2), m.group(2))
    m = re.search(r'layout\.siteName\s*=\s*"([^"]*)"', raw)
    if m:
        site.name = m.group(1)

    m = re.search(r"layout\.wiring\s*=\s*new SiteWiring\s*\{(.*?)\}", raw, re.S)
    if m:
        for k, v in re.findall(r'(\w+)\s*=\s*"([^"]*)"', m.group(1)):
            site.wiring[k] = v

    m = re.search(r"layout\.gates\s*=\s*new WaterGates\s*\{(.*?)\}", raw, re.S)
    if m:
        for k, v in re.findall(r"(\w+)\s*=\s*([\w.]+)f?", m.group(1)):
            site.gates[k] = number(v, site.consts)

    for args in find_calls(raw, "Node"):
        if not args or args[0] != "layout":
            continue
        pos, kw = named(args[1:])
        if len(pos) < 3:
            continue
        node_id = unquote(pos[0])
        has_camera = kw.get("hasCamera", "true").strip() != "false"
        site.nodes[node_id] = Node(node_id, unquote(pos[1]), pos[2], has_camera)

    for args in find_calls(raw, "Link"):
        if not args or args[0] != "layout":
            continue
        pos, kw = named(args[1:])
        if len(pos) < 3:
            continue
        seconds = number(kw.get("seconds", pos[3] if len(pos) > 3 else "0"), site.consts)
        site.links.append(Link(
            a=unquote(pos[0]),
            b=unquote(pos[1]),
            mask=mask_of(pos[2]),
            seconds=seconds,
            min_water=number(kw.get("minWater", "0"), site.consts),
            max_water=number(kw.get("maxWater", "1"), site.consts),
            barrier=unquote(kw.get("barrier", "")),
            one_way=kw.get("oneWay", "false").strip() == "true",
        ))

    for args in find_calls(raw, "StationApproaches"):
        if not args or args[0] != "layout":
            continue
        _, kw = named(args[1:])
        station = next(iter(site.nodes), "STATION")
        approaches = [
            (kw.get("north"), WALK | CRAWL),
            (kw.get("south"), WALK | CRAWL),
            (kw.get("sump"), BURROW | SWIM),
            (kw.get("chase"), CRAWL | CLIMB),
        ]
        for target, mask in approaches:
            if target:
                site.links.append(Link(station, unquote(target), mask, 4.0, 0.0, 1.0, "", False))

    for args in find_calls(raw, "Place"):
        if not args or args[0] != "layout":
            continue
        pos, kw = named(args[1:])
        who = unquote(pos[0])
        home = unquote(kw.get("home", pos[1] if len(pos) > 1 else ""))
        retreat = unquote(kw.get("retreat", pos[2] if len(pos) > 2 else ""))
        rest = pos[1:] if "home" in kw else pos[3:]
        attacks = [unquote(a) for a in rest] if "home" in kw else [unquote(a) for a in pos[3:]]
        site.cast.append(Place(who, home, retreat, attacks))

    return site


# ---------------------------------------------------------------------------
# Checks
# ---------------------------------------------------------------------------

def reachable(site: Site, start: str, capability: int, water: float) -> set:
    """Nodes reachable from `start` at one water level, ignoring barriers."""
    adjacency = {}
    for link in site.links:
        if not link.mask & capability:
            continue
        if not (link.min_water <= water <= link.max_water):
            continue
        adjacency.setdefault(link.a, set()).add(link.b)
        if not link.one_way:
            adjacency.setdefault(link.b, set()).add(link.a)

    seen, stack = {start}, [start]
    while stack:
        for nxt in adjacency.get(stack.pop(), ()):
            if nxt not in seen:
                seen.add(nxt)
                stack.append(nxt)
    return seen


def check(site: Site) -> tuple:
    errors, warnings = [], []
    station = next(iter(site.nodes), None)

    if station is None:
        return ["no nodes at all"], []

    # --- ids -------------------------------------------------------------
    for link in site.links:
        for end in (link.a, link.b):
            if end not in site.nodes:
                errors.append(f"link {link.a}<->{link.b} names unknown node '{end}'")
        if link.seconds <= 0:
            warnings.append(f"link {link.a}<->{link.b} has no traverse time")
        if link.min_water > link.max_water:
            errors.append(
                f"link {link.a}<->{link.b} can never open "
                f"(minWater {link.min_water} > maxWater {link.max_water})")

    for role, node_id in site.wiring.items():
        if node_id and node_id not in site.nodes:
            errors.append(f"wiring.{role} names unknown node '{node_id}'")

    # --- reachability ----------------------------------------------------
    # Anything, anywhere on the water dial. A room nothing can enter at any
    # setting is a room the player will never see.
    everything = WALK | CRAWL | CLIMB | SWIM | BURROW
    seen = set()
    for water in (0.0, 0.25, 0.5, 0.75, 1.0):
        seen |= reachable(site, station, everything, water)

    for node_id in site.nodes:
        if node_id not in seen:
            errors.append(f"node '{node_id}' is unreachable from the station at any water level")

    # --- cast ------------------------------------------------------------
    approaches = {
        site.wiring.get("northApproach"),
        site.wiring.get("southApproach"),
        site.wiring.get("sump"),
        site.wiring.get("chase"),
    } - {None, ""}

    used_approaches = set()

    for place in site.cast:
        for label, node_id in (("home", place.home), ("retreat", place.retreat)):
            if node_id not in site.nodes:
                errors.append(f"{place.who}'s {label} node '{node_id}' is not on this map")

        if not place.attacks:
            errors.append(f"{place.who} has no attack node, so can never threaten the station")

        for node_id in place.attacks:
            if node_id not in site.nodes:
                errors.append(f"{place.who}'s attack node '{node_id}' is not on this map")
            elif node_id not in approaches:
                errors.append(
                    f"{place.who} attacks from '{node_id}', which is not one of this site's "
                    "four approaches — it would never reach a threshold")
            else:
                used_approaches.add(node_id)

    for node_id in sorted(approaches - used_approaches):
        warnings.append(f"approach '{node_id}' is not used by anybody")

    # --- water gates -----------------------------------------------------
    g = site.gates
    if g:
        dig, wade = g.get("diggable", 0), g.get("wadeable", 0)
        swim, drown = g.get("swimmable", 0), g.get("drowned", 1)

        if not dig <= wade:
            errors.append(f"gates: diggable {dig} must not exceed wadeable {wade}")
        if not swim <= drown:
            errors.append(f"gates: swimmable {swim} must not exceed drowned {drown}")
        band = swim - wade
        if band < 0:
            warnings.append(
                f"gates: the dry and wet routes overlap by {-band:.2f} — there is no setting "
                "that closes both")
        elif band > 0.35:
            warnings.append(f"gates: a {band:.2f} safe band is very forgiving")

    # --- cameras ---------------------------------------------------------
    blind = [n.id for n in site.nodes.values() if not n.has_camera]
    if not blind:
        warnings.append("every room has a camera; nothing is left for the seismograph")

    return errors, warnings


def main() -> int:
    if not SITES.is_dir():
        print(f"no site folder at {SITES}", file=sys.stderr)
        return 2

    files = sorted(p for p in SITES.glob("*Layout.cs") if p.name != "LayoutAuthoring.cs")
    if not files:
        print("no layouts found", file=sys.stderr)
        return 2

    total_errors = total_warnings = 0

    for path in files:
        site = parse_site(path)
        if not site.nodes:
            continue

        errors, warnings = check(site)
        total_errors += len(errors)
        total_warnings += len(warnings)

        cameras = sum(1 for n in site.nodes.values() if n.has_camera)
        status = "\033[31mFAIL\033[0m" if errors else "\033[32mok\033[0m"
        print(f"{status}  {site.site_id:<12} {site.name}")
        print(f"      {len(site.nodes)} nodes ({cameras} cameras), "
              f"{len(site.links)} links, {len(site.cast)} characters")

        for e in errors:
            print(f"      \033[31merror\033[0m  {e}")
        for w in warnings:
            print(f"      \033[33mwarn \033[0m  {w}")

    print()
    if total_errors:
        print(f"\033[31m{total_errors} error(s)\033[0m, {total_warnings} warning(s)")
        return 1

    print(f"\033[32mOK\033[0m: {len(files)} site(s), 0 errors, {total_warnings} warning(s)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
