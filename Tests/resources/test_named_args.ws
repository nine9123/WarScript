# --- named arguments ---

fun describe[name, health, damage, speed]
  return name + " hp=" + health + " dmg=" + damage + " spd=" + speed
end

fun spawn[type, x, y]
  return "spawned " + type + " at " + x + "," + y
end

fun calc[a, b, c]
  return a - b * c
end

# Positional (unchanged baseline)
r1 = describe["Orc", 100, 25, 3]
print r1

# Named in declared order (same as positional)
r2 = describe[name: "Orc", health: 100, damage: 25, speed: 3]
print r2

# Named in different order — must produce same result
r3 = describe[health: 100, speed: 3, damage: 25, name: "Orc"]
print r3

# Named multiline (the real use-case)
r4 = describe[
  name: "Goblin",
  health: 40,
  damage: 10,
  speed: 6,
]
print r4

# Named multiline reordered
r5 = describe[
  speed: 6,
  damage: 10,
  name: "Goblin",
  health: 40,
]
print r5

# Named args with expressions
base = 10
r6 = spawn[
  type: "Archer",
  x: base * 2,
  y: base + 5,
]
print r6

# Verify order matters: calc[a, b, c] = a - b * c
# positional:  calc[10, 2, 3] = 10 - 2*3 = 4
# named same:  calc[a: 10, b: 2, c: 3] = 4
# named swapped: calc[c: 3, a: 10, b: 2] = 4
r7 = calc[10, 2, 3]
r8 = calc[a: 10, b: 2, c: 3]
r9 = calc[c: 3, a: 10, b: 2]
print r7
print r8
print r9
