import sys,json
from pathlib import Path
sys.path.insert(0,str(Path('tmp/fonttools').resolve()))
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools import subset
root=Path('android-compose/app/src/main')
codes=json.loads((root/'assets/icon-codepoints.json').read_text())
points=[codes['Symbols.'+s+'_rounded'] for s in ('home','workspaces','settings')]
source=root/'res/font/material_symbols_rounded.ttf'
for fill,name in [(0,'outline'),(1,'filled')]:
    font=TTFont(source)
    axes={a.axisTag:a.defaultValue for a in font['fvar'].axes}
    axes.update(FILL=fill,wght=400,GRAD=0,opsz=24)
    instantiateVariableFont(font,axes,inplace=True)
    outlines={cp:font['glyf'][font.getBestCmap()[cp]].getCoordinates(font['glyf'])[0] for cp in points}
    cutter=subset.Subsetter(options=subset.Options());cutter.populate(unicodes=points);cutter.subset(font)
    dest=root/f'res/font/glass_symbols_{name}.ttf';font.save(dest)
    check=TTFont(dest)
    assert 'fvar' not in check
    assert all(check['glyf'][check.getBestCmap()[cp]].getCoordinates(check['glyf'])[0] == outlines[cp] for cp in points)
    print(dest.name,dest.stat().st_size,'bytes; three source glyph outlines verified')
