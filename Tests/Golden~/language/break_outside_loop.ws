# T3.10 (plan 5.6, PROP-29): 'break' and 'next' outside a loop are compile errors.
fun f()
    break   # expect-compile-error: 'break' outside of a loop
end
f()
