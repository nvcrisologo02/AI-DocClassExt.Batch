# Cambios en DocumentIA.Batch.Classification UI

## Resumen de mejoras

Se han realizado cambios en la interfaz de usuario de `DocumentIA.Batch.Classification` para mejorar la experiencia de visualización y el control de espacios.

### Cambios implementados:

#### 1. **Secciones colapsables (Expanders)**
   - **Summary (selección actual)** - 🧭
   - **Justificación del camino de clasificación** - 📝
   - **Traza de actividades** - ⏱️

Todas las secciones se encuentran inicialmente **expandidas** pero pueden colapsarse haciendo clic en el encabezado para ganar espacio visible.

#### 2. **Redimensionamiento de paneles**
   - Se agregó un `GridSplitter` entre la tabla de "Classified Documents" y las secciones de detalles
   - Permite **arrastrar el divisor hacia arriba/abajo** para ajustar el tamaño de cada panel según necesidad
   - La tabla de documentos usa `Height="1.5*"` (mayor altura predeterminada)
   - Las secciones de detalles usan `Height="*"` (espacio restante)

#### 3. **Mejora visual**
   - ScrollViewer añadido en el área de detalles para mejor navegación
   - Estilos consistentes con colores y bordes de la aplicación original
   - Iconos descriptivos en los encabezados de cada sección

## Estructura XAML actualizada

```
Grid.Column="2" (panel derecho):
├── Row 0: KPIs (Total, Completed, Errors, Processing)
├── Row 1: Encabezado "Classified Documents"
├── Row 2: DataGrid con archivos (Height="1.5*")
├── Row 3: GridSplitter (para redimensionar)
└── Row 4: ScrollViewer con Expanders (Height="*")
    ├── Expander: Summary
    ├── Expander: Justificación
    └── Expander: Traza de actividades
```

## Beneficios

1. **Mayor claridad visual**: Colapsando secciones innecesarias, se mejora el enfoque
2. **Control flexible**: Redimensionamiento dinámico según prioridades del usuario
3. **Mejor navegación**: ScrollViewer permite explorar detalles sin ocupar más espacio
4. **Interfaz intuitiva**: Expanders claramente identificados con iconos y títulos

## Archivos modificados

- `MainWindow.xaml`: Reestructuración del Grid y adición de Expanders/GridSplitter

## Compilación

✅ Compilación exitosa: `DocumentIA.Batch.Classification realizado correctamente`

---

**Nota**: Los estilos de Expander mantienen consistencia con el diseño de la aplicación original (colores, bordes, fuentes).
