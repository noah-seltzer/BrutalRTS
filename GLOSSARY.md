# BrutalRTS

A small real-time strategy game: players gather crystals, build structures, train units, and destroy the opposing base.

## Combat

**Attribute**:
A tag on a unit or structure that bonus damage can target. The attributes are Light, Armored and Structure; an entity may carry more than one (a structure is Armored and Structure).
_Avoid_: Damage type, armor type, class

**Armor**:
A flat amount subtracted from every hit an entity takes. A hit always deals at least 1 damage.
_Avoid_: Defense, damage reduction

**Bonus damage**:
Extra damage an attacker deals per hit against targets carrying a specific attribute.
_Avoid_: Damage multiplier, counter damage

**Counter**:
A unit whose bonus damage or armor makes it cost-effective against another unit.

**Veterancy**:
The rank (0–3) an entity earns from kills. Purely cosmetic: it is displayed but never changes stats.
_Avoid_: Experience, level
