# T3.3 (5.8): an import inside a block or a body is an error.
fun f []
    import "lib"   # expect-compile-error: 'import' is allowed only at the top of a file
end
