# compare.py -- out.png dirA dirB view pose,pose,...   (rows = poses; columns = A then B)
import bpy, sys, numpy as np
a=sys.argv[sys.argv.index('--')+1:]; out,A,B,views,poses=a[0],a[1],a[2],a[3].split(','),a[4].split(',')
def load(p):
    im=bpy.data.images.load(p); w,h=im.size; px=np.array(im.pixels[:],np.float32).reshape(h,w,4); bpy.data.images.remove(im); return px
tiles=[[load(f'{d}/{p}_{v}.png') for v in views for d in (A,B)] for p in poses]
h,w=tiles[0][0].shape[:2]; gap=12
cols=len(tiles[0]); W=cols*w+(cols//2-1)*gap*2+gap*(cols//2)
S=np.ones((len(poses)*h,cols*w+gap*(cols-1),4),np.float32)
for r,row in enumerate(tiles):
    for c,t in enumerate(row):
        x=c*(w+gap); y=(len(poses)-1-r)*h
        S[y:y+h,x:x+w]=t
        if c%2==1: S[y:y+h,x-gap:x]=[0.9,0.2,0.2,1] if False else [0.15,0.15,0.15,1]
o=bpy.data.images.new('o',S.shape[1],S.shape[0]); o.pixels=S.ravel(); o.filepath_raw=out; o.file_format='PNG'; o.save()
