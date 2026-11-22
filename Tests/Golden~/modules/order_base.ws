# T3.10 (plan 5.8): imported by order_left.ws and order_right.ws, which
# import_order.ws imports. Its top level runs once per instance.
print "base"                                  # expect: base

fun base_name []
    return "base"
end
