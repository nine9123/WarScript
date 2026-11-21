# T3.4 (PROP-10): `global` must come before any use of the name in its body.
score = 0
fun f []
    print score
    global score   # expect-compile-error: 'global score' must come before any use of 'score' in this body
    score = 1
end
