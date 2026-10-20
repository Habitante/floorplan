; C#: types and their members; namespaces hold them but do not name them.
(namespace_declaration name: (_) @name body: (declaration_list) @body) @definition.namespace
(class_declaration name: (identifier) @name body: (declaration_list) @body) @definition.class
(struct_declaration name: (identifier) @name body: (declaration_list) @body) @definition.struct
(record_declaration name: (identifier) @name body: (declaration_list) @body) @definition.record
(record_declaration name: (identifier) @name !body) @definition.record
(interface_declaration name: (identifier) @name body: (declaration_list) @body) @definition.interface
(enum_declaration name: (identifier) @name body: (enum_member_declaration_list) @body) @definition.enum
(method_declaration name: (identifier) @name body: (_) @body) @definition.method
(method_declaration name: (identifier) @name !body) @definition.method
(constructor_declaration name: (identifier) @name body: (_) @body) @definition.constructor
(property_declaration name: (identifier) @name accessors: (accessor_list) @body) @definition.property
(property_declaration name: (identifier) @name) @definition.property
(field_declaration (variable_declaration (variable_declarator name: (identifier) @name))) @definition.field
(event_field_declaration (variable_declaration (variable_declarator name: (identifier) @name))) @definition.event
(delegate_declaration name: (identifier) @name) @definition.delegate
(local_function_statement name: (identifier) @name body: (_) @body) @definition.function
(operator_declaration body: (_) @body) @definition.operator
