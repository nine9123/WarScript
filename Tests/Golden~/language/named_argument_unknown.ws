# T3.8 (PROP-29): a named argument must name a parameter of the called
# function (v1 dropped it silently).
fun spawn [team, hp]
    return team + " " + hp
end
print spawn [team: "Red", health: 10]   # expect-compile-error: Function 'spawn' has no parameter 'health'
