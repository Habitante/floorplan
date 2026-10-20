; GDScript: the script is a class; its members, inner classes and theirs.
(signal_statement name: (name) @name) @definition.signal
(source (variable_statement name: (name) @name) @definition.variable)
(class_body (variable_statement name: (name) @name) @definition.variable)
(source (export_variable_statement name: (name) @name) @definition.variable)
(class_body (export_variable_statement name: (name) @name) @definition.variable)
(source (onready_variable_statement name: (name) @name) @definition.variable)
(class_body (onready_variable_statement name: (name) @name) @definition.variable)
(source (const_statement name: (name) @name) @definition.constant)
(class_body (const_statement name: (name) @name) @definition.constant)
(enum_definition name: (name) @name body: (enumerator_list) @body) @definition.enum
(enum_definition !name body: (enumerator_list) @body) @definition.enum
(function_definition name: (name) @name body: (body) @body) @definition.function
(constructor_definition body: (body) @body) @definition.constructor
(class_definition name: (name) @name body: (class_body) @body) @definition.class
