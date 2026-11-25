# T3.3 (PROP-10): a top-level block is still the top level.
if true
    global x   # expect-compile-error: 'global' is allowed only in a function, method, lambda or class body
end
