# T3.8 (PROP-29): a named argument may be given once (v1 kept the last).
fun spawn(team, hp)
    return team + " " + hp
end
print spawn(team: "Red", hp: 10, team: "Blue")   # expect-compile-error: Argument 'team' is given twice
