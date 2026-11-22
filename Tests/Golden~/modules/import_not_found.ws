# T3.10 (plan 5.8): a path the host cannot resolve is a compile error.
import "no_such_module"   # expect-compile-error: Import 'no_such_module' not found
