# T3.10 (plan 5.8, PROP-12): one namespace for the module set, so a function
# of an import cannot be defined again with an overlapping arity.
import "order_base"
fun base_name()   # expect-compile-error: Function 'base_name' with 0 arguments is already defined in 'order_base'
    return "main"
end
