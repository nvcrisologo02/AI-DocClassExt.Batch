using Xunit;

// Las clases de test crean bases SQLite temporales y llaman a SqliteConnection.ClearAllPools()
// en su Dispose (necesario para liberar el fichero y borrar el directorio temporal en Windows).
// ClearAllPools() es global al proceso: si xUnit ejecuta clases en paralelo, cerrar los pools
// de una clase invalida las conexiones abiertas de otra (ObjectDisposedException intermitente).
// Serializar las clases elimina esa carrera; la suite es pequena y el coste es despreciable.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
