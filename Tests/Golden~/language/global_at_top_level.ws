# T3.3 (PROP-10): at the top level every assignment already writes a global.
x = 1
global x   # expect-compile-error: 'global' is allowed only in a function, method, lambda or class body
