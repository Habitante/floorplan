; TypeScript and TSX: JavaScript's declarations plus types, interfaces and enums.
(function_declaration name: (identifier) @name body: (statement_block) @body) @definition.function
(generator_function_declaration name: (identifier) @name body: (statement_block) @body) @definition.function
(function_signature name: (_) @name) @definition.function
(class_declaration name: (_) @name body: (class_body) @body) @definition.class
(abstract_class_declaration name: (_) @name body: (class_body) @body) @definition.class
(interface_declaration name: (_) @name body: (interface_body) @body) @definition.interface
(type_alias_declaration name: (_) @name) @definition.type
(enum_declaration name: (_) @name body: (enum_body) @body) @definition.enum
(method_definition name: (_) @name body: (statement_block) @body) @definition.method
(public_field_definition name: (_) @name) @definition.field
(program (lexical_declaration (variable_declarator name: (identifier) @name value: [(arrow_function body: (statement_block) @body) (function_expression body: (statement_block) @body)])) @definition.function)
(program (lexical_declaration (variable_declarator name: (identifier) @name)) @definition.variable)
(export_statement declaration: (lexical_declaration (variable_declarator name: (identifier) @name value: [(arrow_function body: (statement_block) @body) (function_expression body: (statement_block) @body)])) @definition.function)
(export_statement declaration: (lexical_declaration (variable_declarator name: (identifier) @name)) @definition.variable)
((expression_statement (call_expression function: (identifier) @_callee arguments: (arguments . (string (string_fragment) @name) . [(arrow_function body: (statement_block) @body) (function_expression body: (statement_block) @body)]))) @definition.test
  (#any-of? @_callee "describe" "it" "test" "suite"))
; An object literal whose values include methods names their scope, like a class.
(pair key: (_) @name value: (object (method_definition)) @body) @definition.object
