# Named arguments are matched to the parameters by name; a parameter given
# no argument is null, which takes its default (T3.8 rejects unknown or
# repeated names: named_argument_unknown.ws, named_argument_twice.ws).
fun spawn [team, hp = 100, name = "unit"]
    return "{name}/{team}/{hp}"
end
print spawn [hp: 5, team: "Red"]
print spawn [name: "Scout", team: "Blue", hp: 1]
print spawn [team: "Green"]
# expect: unit/Red/5
# expect: Scout/Blue/1
# expect: unit/Green/100
