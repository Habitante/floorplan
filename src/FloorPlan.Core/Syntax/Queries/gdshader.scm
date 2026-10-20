; Godot shaders: the uniforms and varyings are the data; functions include vertex(), fragment() and light().
(uniform_declaration specifier: (var_specifier name: (ident) @name)) @definition.uniform
(varying_declaration specifier: (var_specifier name: (ident) @name)) @definition.varying
(source_file (const_declaration specifier: (var_specifier name: (ident) @name)) @definition.constant)
(struct_declaration name: (_) @name members: (_) @body) @definition.struct
(function_declaration name: (ident) @name (block) @body) @definition.function
