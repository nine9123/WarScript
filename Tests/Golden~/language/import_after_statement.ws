# T3.3 (5.8): imports come before every other statement of a file
# (comments may come first). The migration tool moves the ones it can.
x = 1
import "lib"   # expect-compile-error: 'import' must come before every other statement
