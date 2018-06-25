```
__declspec(noinline)
float test_sse(const float * pa, const float * pb, int c)
{
	const float * paLim = pa + c;
	__m128 res = _mm_setzero_ps();
	for (; pa < paLim; pa++, pb++)
		res = _mm_add_ss(res, _mm_mul_ss(_mm_load_ss(pa), _mm_load_ss(pb)));
	return _mm_cvtss_f32(res);
}

__declspec(noinline)
float test_scalar(const float * pa, const float * pb, int c)
{
	const float * paLim = pa + c;
	float res = 0;
	for (; pa < paLim; pa++, pb++)
		res = res + (*pa) * (*pb);
	return res;
}
```

VS2017 x64 release build, the asm looks like following

![alt text](https://github.com/helloguo/notes/blob/master/pic/Capturesse.PNG)

![alt text](https://github.com/helloguo/notes/blob/master/pic/Capturescalar.PNG)

