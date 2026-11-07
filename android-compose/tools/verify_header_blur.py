# Verify the API24-30 header blur against discrete Gaussian convolution.
from pathlib import Path
import math
import runpy
import os
os.environ['HINGE_GLASS_TEST_DPR']='1'
gl=runpy.run_path(str(Path(__file__).with_name('verify_glass_render.py')))
w,h=gl['W'],gl['H']
row=b''.join(bytes((255 if x>=w//2 else 0,)*3+(255,)) for x in range(w))
gl['upload'](0,row*h)
for sigma in (20,40,60,80):
    gl['blur'](0,None,sigma/2)
    data=gl['read']()
    radius=math.ceil(3*sigma)
    weights=[math.exp(-i*i/(2*sigma*sigma)) for i in range(-radius,radius+1)]
    total=sum(weights)
    errors=[]
    for x in range(0,w,7):
        expected=255*sum(v for i,v in zip(range(-radius,radius+1),weights) if x+i>=w//2)/total
        actual=gl['pixel'](data,x,h//2)
        assert actual[0]==actual[1]==actual[2] and actual[3]==255,actual
        errors.append(abs(actual[0]-expected))
    assert max(errors)<1.1,(sigma,max(errors))
    print('Header Gaussian sigma',sigma,'max channel error',round(max(errors),4),'/255')
