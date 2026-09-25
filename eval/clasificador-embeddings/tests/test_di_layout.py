import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from di_layout import paginas_facturables, rango_paginas


def test_recorte():
    assert paginas_facturables(12, 5) == 5
    assert paginas_facturables(3, 5) == 3


def test_paginas_desconocidas_cuentan_el_maximo():
    assert paginas_facturables(-1, 5) == 5


def test_rango_paginas_recorte():
    assert rango_paginas(2, 5) == "1-2"
    assert rango_paginas(12, 5) == "1-5"


def test_rango_paginas_desconocidas():
    assert rango_paginas(-1, 5) == "1-5"
