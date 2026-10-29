import numpy as np
from PIL import Image
import os
# Cuts Kenny's controller art (docs/art/controller/) into the phone page's sprites (Assets/StreamingAssets/phone/).
# The gems come out blank: the baked A/B/X/Y and SPRINT/PASS text is painted out with a smooth fit of the face's own
# shading, so layouts.js can write whatever each button does. Orange and blue are recoloured red and purple.
# Run: python tools/phone/cut-controller-art.py   (needs Pillow and numpy)
ROOT=os.path.join(os.path.dirname(os.path.abspath(__file__)),'..','..')
U=os.path.join(ROOT,'docs','art','controller')+os.sep
OUT=os.path.join(ROOT,'Assets','StreamingAssets','phone')+os.sep
btn=np.array(Image.open(U+'buttons.png').convert('RGBA')).astype(np.float32)

def box(a, r):
    # box blur (separable) via cumsum, edge-clamped, radius r
    if r<1: return a
    p=np.pad(a, ((r+1,r),(0,0))+((0,0),)*(a.ndim-2), mode='edge')
    c=np.cumsum(p,0); a1=(c[2*r+1:]-c[:-2*r-1])/(2*r+1)
    p=np.pad(a1, ((0,0),(r+1,r))+((0,0),)*(a.ndim-2), mode='edge')
    c=np.cumsum(p,1); return (c[:,2*r+1:]-c[:,:-2*r-1])/(2*r+1)
def blur(a,r):
    for _ in range(3): a=box(a,r)
    return a

def fill(rgb, known):
    w=known.astype(np.float32)
    out=rgb.copy(); done=known.copy()
    for r in [2,4,7,12,20,32,50]:
        num=blur(rgb*w[...,None], r); den=blur(w, r)
        est=num/np.maximum(den,1e-6)[...,None]
        take=(~done)&(den>0.25)
        out[take]=est[take]; done|=take
    out[~done]=est[~done]
    return out

def square(img, pad=6):
    h,w=img.shape[:2]; s=max(h,w)+2*pad
    o=np.zeros((s,s,4),np.float32); y=(s-h)//2; x=(s-w)//2
    o[y:y+h,x:x+w]=img; return o

def save(a, name, size):
    im=Image.fromarray(np.clip(a,0,255).astype(np.uint8),'RGBA').resize((size,size), Image.LANCZOS)
    im.save(OUT+name, optimize=True); print(name, im.size)

gems={'teal':(94,25,610,531),'red':(647,46,1120,516),'purple':(132,571,584,1017),'yellow':(646,571,1105,1026)}
FACE=0.80
TH={'teal':60,'red':60,'purple':55,'yellow':60}   # face radius / outer radius (inside the gold rim)
blank={}
for name,(x0,y0,x1,y1) in gems.items():
    g=square(btn[y0:y1+1,x0:x1+1].copy())
    s=g.shape[0]; cy=cx=(s-1)/2
    # the real circle: from the opaque extent
    al=g[...,3]>128; ys,xs=np.where(al); cy=(ys.min()+ys.max())/2; cx=(xs.min()+xs.max())/2; R=(xs.max()-xs.min())/2
    yy,xx=np.mgrid[0:s,0:s]; d=np.hypot(yy-cy,xx-cx)
    rgb=g[...,:3]; mx=rgb.max(-1); mn=rgb.min(-1)
    inner=d< R*FACE
    dark=mx<95
    cream=(mn>170)&((mx-mn)<75)
    if name=='red': dark|=(rgb[...,0]<150)&(rgb[...,1]<40)&(d<R*0.62)   # B's dark red drop shadow
    text=(dark|cream)&inner
    # grow the mask a little to take the anti-aliased edges and outlines
    tm=blur(text.astype(np.float32),3)>0.02
    tm&=d<R*FACE*0.97
    known=~tm
    rgb2=fill(rgb, known)
    for it in range(3):
        # anything left that stands out from its smoothed surroundings is text residue (outlines, shadows)
        ref=fill(rgb, known & (blur((np.abs(rgb-blur(rgb2,12)).sum(-1)>70).astype(np.float32),2)<0.02))
        diff=np.abs(rgb-blur(ref,6)).sum(-1)
        extra=(diff>TH[name])&(d<R*FACE*0.9)
        tm|=blur(extra.astype(np.float32),3)>0.02
        tm&=d<R*FACE*0.97
        known=~tm
        rgb2=fill(rgb, known)
    g[...,:3]=np.where(tm[...,None], rgb2, rgb)
    # solid: fill each row between its outermost text pixels, so no letter outline survives as a ghost
    sol=tm.copy()
    for y in range(s):
        xs_=np.where(tm[y])[0]
        if len(xs_): sol[y,xs_.min():xs_.max()+1]=True
    sol=(blur(sol.astype(np.float32),4)>0.02)&(d<R*FACE*0.97)
    # fill the text area with a smooth fit of the face's own shading (light top-left, dark bottom-right)
    fit=(~sol)&(d<R*FACE*0.95)
    nx=(xx-cx)/R; ny=(yy-cy)/R
    def basis(X,Y): return np.stack([X**i*Y**j for i in range(4) for j in range(4-i)],-1)
    A=basis(nx[fit],ny[fit]); est=np.empty_like(rgb)
    Bm=basis(nx,ny)
    for ch in range(3):
        coef,*_=np.linalg.lstsq(A, rgb[...,ch][fit], rcond=None); est[...,ch]=Bm@coef
    soft=np.clip(blur(sol.astype(np.float32),5)*1.6,0,1)*(d<R*FACE*0.97)
    g[...,:3]=rgb*(1-soft[...,None])+est*soft[...,None]
    blank[name]=(g,inner,d,R)
    print(name, 'R',R, 'masked px', tm.sum())

def rgb2hsv(c):
    c=c/255.0; r,g,b=c[...,0],c[...,1],c[...,2]
    mx=c.max(-1); mn=c.min(-1); df=mx-mn+1e-9
    h=np.where(mx==r,(g-b)/df%6,np.where(mx==g,(b-r)/df+2,(r-g)/df+4))/6
    s=np.where(mx>0,df/np.maximum(mx,1e-9),0); return h,s,mx
def hsv2rgb(h,s,v):
    i=np.floor(h*6)%6; f=h*6-np.floor(h*6); p=v*(1-s); q=v*(1-f*s); t=v*(1-(1-f)*s)
    out=np.zeros(h.shape+(3,))
    for k,(a,b,c) in enumerate([(v,t,p),(q,v,p),(p,v,t),(p,q,v),(t,p,v),(v,p,q)]):
        m=i==k; out[m,0]=a[m]; out[m,1]=b[m]; out[m,2]=c[m]
    return out*255
def recolor(src, dh, sat=1.0, val=1.0):
    g,inner,d,R=blank[src]; g=g.copy()
    h,s,v=rgb2hsv(g[...,:3]); m=d<R*FACE*0.985
    h2=(h+dh/360)%1; s2=np.clip(s*sat,0,1); v2=np.clip(v*val,0,1)
    g[...,:3]=np.where(m[...,None], hsv2rgb(h2,s2,v2), g[...,:3]); return g

S=256
for n in gems: save(blank[n][0], 'gem-'+n+'.png', S)
save(recolor('red', 24, 1.0, 1.06), 'gem-orange.png', S)
save(recolor('purple', -38, 1.0, 1.05), 'gem-blue.png', S)
# the PAUSE plate as it is (PAUSE never changes). The gear is not used yet: the phone has no settings screen.
p=btn[1060:1274,170:735].copy()
Image.fromarray(np.clip(p,0,255).astype(np.uint8)).resize((300,int(300*p.shape[0]/p.shape[1])),Image.LANCZOS).save(OUT+'pause.png',optimize=True)

joy=np.array(Image.open(U+'joystick.png').convert('RGBA')).astype(np.float32)
save(square(joy[11:821,179:1010].copy(),4), 'stick-base.png', 400)
cap=square(joy[11:821,1156:1629].copy(),4)
# the cap's bbox rows: find them
al=cap[...,3]>40; ys,xs=np.where(al); cap=square(cap[ys.min():ys.max()+1, xs.min():xs.max()+1],4)
save(cap,'stick-cap.png',220)
# a gold cap for the throw stick: tint the ivory face toward gold
s_=cap.shape[0]; yy,xx=np.mgrid[0:s_,0:s_]; dd=np.hypot(yy-(s_-1)/2,xx-(s_-1)/2); Rc=(s_-8)/2
h,s,v=rgb2hsv(cap[...,:3]); m=(dd<Rc*0.83)
gold=cap.copy(); gold[...,:3]=np.where(m[...,None], hsv2rgb(np.full_like(h,0.125), np.clip(s*1.0+0.55,0,1), np.clip(v*1.0,0,1)), cap[...,:3])
save(gold,'stick-cap-gold.png',220)

# the grass under the play pictures: the field background with its chalk lines painted out (huddle.js draws its own)
f=np.array(Image.open(U+'field-bg.png').convert('RGB')).astype(np.float32)
mx=f.max(-1); mn=f.min(-1); chalk=(mn>170)
rows=chalk.mean(1)>0.05
band=np.where(rows)[0]
lines=[]; 
for y in band:
    if lines and y<=lines[-1][1]+1: lines[-1][1]=y
    else: lines.append([y,y])
for y0,y1 in lines:
    y0-=6; y1+=6; h=y1-y0+1
    f[y0:y1+1]=f[y0-h:y0]          # the strip of grass just above
# the little end marks of each line sit at the edges: crop them off
f=f[:, 40:-40]
Image.fromarray(np.clip(f,0,255).astype(np.uint8)).resize((512,int(512*f.shape[0]/f.shape[1])),Image.LANCZOS).save(OUT+'turf.png',optimize=True)
print('turf.png', len(lines),'lines painted out')
