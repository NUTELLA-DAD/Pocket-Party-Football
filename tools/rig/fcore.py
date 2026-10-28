# Pure numpy core: retract a hand's extra finger and build thumb/finger bones and weights (T-pose, arms along X,
# model faces -Y, Z up; one side at a time, x mirrored by s). Shared by the preview and the Blender script.
import numpy as np
def inpoly(pts,poly):
    poly=np.array(poly); x,y=pts[:,0],pts[:,1]; ins=np.zeros(len(pts),bool)
    for i in range(len(poly)):
        (x1,y1),(x2,y2)=poly[i],poly[i-1]
        ins^=((y1>y)!=(y2>y))&(x<(x2-x1)*(y-y1)/(y2-y1+1e-12)+x1)
    return ins
def ramp(a): return np.clip(a,0,1)
def retract(co,s,p,keep=0.25):
    co=co.copy()
    if 'extra' not in p: return co,np.zeros(len(co),bool)
    e=inpoly(np.stack([co[:,0]*s,co[:,1]],1),p['extra'])
    if e.any():
        b=np.array(p['base']); zb=co[e,2].mean(); xy=np.stack([co[e,0]*s,co[e,1]],1); xy=b+(xy-b)*keep
        co[e,0]=xy[:,0]*s; co[e,1]=xy[:,1]; co[e,2]=zb+(co[e,2]-zb)*0.4
    return co,e
def rig(co,h,s,p,side):
    x=co[:,0]*s; y=co[:,1]; hand=h>0
    (xa,ya),(xb,yb)=p['thumb']; ly=ya+(yb-ya)*(x-xa)/(xb-xa)
    t=ramp((ly-y)/0.008+0.5)*ramp((x-p['thumbX'])/0.015)*ramp((p['thumbXmax']-x)/0.01)
    A,B=np.array(p['kline'][0]),np.array(p['kline'][1]); d=B-A; n=np.array([d[1],-d[0]])/np.linalg.norm(d)
    if n[0]<0: n=-n
    sd=(x-A[0])*n[0]+(y-A[1])*n[1]
    f=ramp((sd+0.008)/0.016)*(1-t)
    dist=ramp((sd-p['midOff']+0.008)/0.016)
    n3=np.array([n[0]*s,n[1],0.0])
    kn=hand&(np.abs(sd)<0.006)&(f>0.3)&(h>0.5)
    head=co[kn].mean(0); mid=head+n3*p['midOff']; tipd=sd[hand&(f>0.5)].max(); tip=head+n3*tipd
    # the thumb: its own axis (PCA of the thumb), root at the wrist end
    tv=co[hand&(t>0.9)&(h>0.5)]; c=tv.mean(0); ax=np.linalg.svd(tv-c)[2][0]
    if (ax[0]*s)<0: ax=-ax          # pointing out along the arm
    u=(co-c)@ax; ut=u[hand&(t>0.9)]; u0,u1=np.percentile(ut,5),np.percentile(ut,98)
    um=u0+0.55*(u1-u0); un=(u-u0)/(u1-u0)
    th_head=c+ax*(u0+0.1*(u1-u0)); th_mid=c+ax*um; th_tip=c+ax*u1
    tdist=ramp((un-0.55+0.1)/0.2)
    W={f'{side}Fingers':h*f*(1-dist), f'{side}FingersTip':h*f*dist, f'{side}Thumb':h*t*(1-tdist), f'{side}ThumbTip':h*t*tdist}
    newh=h*(1-f-t)
    B={f'{side}Fingers':(head,mid,f'{side}Hand'), f'{side}FingersTip':(mid,tip,f'{side}Fingers'),
       f'{side}Thumb':(th_head,th_mid,f'{side}Hand'), f'{side}ThumbTip':(th_mid,th_tip,f'{side}Thumb')}
    return W,newh,B
